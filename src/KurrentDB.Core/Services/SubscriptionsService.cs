// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.TimerService;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services;

public enum SubscriptionDropReason {
	Unsubscribed = 0,
	AccessDenied = 1,
	NotFound = 2,
	PersistentSubscriptionDeleted = 3,
	SubscriberMaxCountReached = 4,
	StreamDeleted = 5
}

public abstract class SubscriptionsService {
	public const string AllStreamsSubscriptionId = ""; // empty stream id means subscription to all streams
	protected static readonly ILogger Log = Serilog.Log.ForContext<SubscriptionsService>();
}

public class SubscriptionsService<TStreamId> :
	SubscriptionsService,
	IHandle<SystemMessage.SystemStart>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<TcpMessage.ConnectionClosed>,
	IAsyncHandle<ClientMessage.SubscribeToStream>,
	IAsyncHandle<ClientMessage.FilteredSubscribeToStream>,
	IHandle<ClientMessage.UnsubscribeFromStream>,
	IHandle<SubscriptionMessage.DropSubscription>,
	IHandle<SubscriptionMessage.PollStream>,
	IHandle<SubscriptionMessage.CheckPollTimeout>,
	IAsyncHandle<StorageMessage.InMemoryEventCommitted>,
	IAsyncHandle<StorageMessage.EventCommitted> {

	private const int DontReportCheckpointReached = -1;

	// ReSharper disable once StaticMemberInGenericType
	private static readonly TimeSpan TimeoutPeriod = TimeSpan.FromSeconds(1);
	// ReSharper disable once StaticMemberInGenericType
	private static readonly char[] LinkToSeparator = ['@'];

	private readonly Dictionary<string, List<Subscription>> _subscriptionTopics = new();
	private readonly Dictionary<Guid, Subscription> _subscriptionsById = new();
	private readonly Dictionary<string, List<PollSubscription>> _pollTopics = new();

	private long _lastSeenCommitPosition = -1;
	private long _lastSeenInMemoryCommitPosition = -1;

	private readonly IPublisher _bus;
	private readonly IEnvelope _busEnvelope;
	private readonly IQueuedHandler _queuedHandler;
	private readonly IReadIndex<TStreamId> _readIndex;
	private readonly IVirtualStreamReader _virtualStreamReader;
	private readonly IAuthorizationProvider _authorizationProvider;

	public SubscriptionsService(
		IPublisher bus,
		IQueuedHandler queuedHandler,
		IAuthorizationProvider authorizationProvider,
		IReadIndex<TStreamId> readIndex,
		IVirtualStreamReader inMemReader) {
		_bus = Ensure.NotNull(bus);
		_busEnvelope = bus;
		_queuedHandler = Ensure.NotNull(queuedHandler);
		_readIndex = Ensure.NotNull(readIndex);
		_virtualStreamReader = Ensure.NotNull(inMemReader);
		_authorizationProvider = Ensure.NotNull(authorizationProvider);
	}

	public void Handle(SystemMessage.SystemStart message) {
		_bus.Publish(TimerMessage.Schedule.Create(TimeoutPeriod, _busEnvelope, new SubscriptionMessage.CheckPollTimeout()));
	}

	/* SUBSCRIPTION SECTION */
	public void Handle(SystemMessage.BecomeShuttingDown message) {
		List<Subscription> subscriptions = _subscriptionsById.Values.ToList();
		foreach (var subscription in subscriptions) {
			DropSubscription(subscription, SubscriptionDropReason.Unsubscribed, sendDropNotification: true);
		}

		_queuedHandler.RequestStop();
	}

	public void Handle(TcpMessage.ConnectionClosed message) {
		List<string> subscriptionGroupsToRemove = null;
		foreach (var (key, subscriptions) in _subscriptionTopics) {
			for (int i = 0, n = subscriptions.Count; i < n; ++i) {
				if (subscriptions[i].ConnectionId == message.Connection.ConnectionId)
					_subscriptionsById.Remove(subscriptions[i].CorrelationId);
			}

			subscriptions.RemoveAll(x => x.ConnectionId == message.Connection.ConnectionId);
			if (subscriptions.Count == 0) // schedule removal of list instance
			{
				subscriptionGroupsToRemove ??= [];
				subscriptionGroupsToRemove.Add(key);
			}
		}

		if (subscriptionGroupsToRemove != null) {
			for (int i = 0, n = subscriptionGroupsToRemove.Count; i < n; ++i) {
				_subscriptionTopics.Remove(subscriptionGroupsToRemove[i]);
			}
		}
	}

	async ValueTask IAsyncHandle<ClientMessage.SubscribeToStream>.HandleAsync(ClientMessage.SubscribeToStream msg, CancellationToken token) {
		var isVirtualStream = SystemStreams.IsVirtualStream(msg.EventStreamId);

		long? lastEventNumber = null;
		if (isVirtualStream) {
			lastEventNumber = _virtualStreamReader.GetLastEventNumber(msg.EventStreamId);
		} else if (!msg.EventStreamId.IsEmptyString()) {
			lastEventNumber = await _readIndex.GetStreamLastEventNumber(_readIndex.GetStreamId(msg.EventStreamId), token);
		}

		if (lastEventNumber == EventNumber.DeletedStream) {
			msg.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(Guid.Empty, SubscriptionDropReason.StreamDeleted));
			return;
		}

		var lastIndexedPos = isVirtualStream
			? _virtualStreamReader.GetLastIndexedPosition(msg.EventStreamId)
			: _readIndex.LastIndexedPosition;

		SubscribeToStream(msg.CorrelationId, msg.Envelope, msg.ConnectionId, msg.EventStreamId,
			msg.ResolveLinkTos, lastIndexedPos, lastEventNumber,
			msg.User, msg.EventStreamId.IsEmptyString() ? EventFilter.DefaultAllFilter : EventFilter.DefaultStreamFilter);

		var subscribedMessage =
			new ClientMessage.SubscriptionConfirmation(msg.CorrelationId, lastIndexedPos, lastEventNumber);
		msg.Envelope.ReplyWith(subscribedMessage);
	}

	async ValueTask IAsyncHandle<ClientMessage.FilteredSubscribeToStream>.HandleAsync(ClientMessage.FilteredSubscribeToStream msg, CancellationToken token) {
		var isVirtualStream = SystemStreams.IsVirtualStream(msg.EventStreamId);

		long? lastEventNumber = null;
		if (isVirtualStream) {
			lastEventNumber = _virtualStreamReader.GetLastEventNumber(msg.EventStreamId);
		} else if (!msg.EventStreamId.IsEmptyString()) {
			lastEventNumber = await _readIndex.GetStreamLastEventNumber(_readIndex.GetStreamId(msg.EventStreamId), token);
		}

		var lastIndexedPos = isVirtualStream
			? _virtualStreamReader.GetLastIndexedPosition(msg.EventStreamId)
			: _readIndex.LastIndexedPosition;

		SubscribeToStream(msg.CorrelationId, msg.Envelope, msg.ConnectionId, msg.EventStreamId,
			msg.ResolveLinkTos, lastIndexedPos, lastEventNumber, msg.User, msg.EventFilter,
			msg.CheckpointInterval, msg.CheckpointIntervalCurrent);
		var subscribedMessage = new ClientMessage.SubscriptionConfirmation(msg.CorrelationId, lastIndexedPos, lastEventNumber);
		msg.Envelope.ReplyWith(subscribedMessage);
	}

	public void Handle(ClientMessage.UnsubscribeFromStream message) {
		DropSubscription(message.CorrelationId, SubscriptionDropReason.Unsubscribed);
	}

	public void Handle(SubscriptionMessage.DropSubscription message) {
		DropSubscription(message.SubscriptionId, message.DropReason);
	}

	private void SubscribeToStream(Guid correlationId, IEnvelope envelope, Guid connectionId,
		string eventStreamId, bool resolveLinkTos, long lastIndexedPosition, long? lastEventNumber,
		ClaimsPrincipal user,
		IEventFilter eventFilter, int? checkpointInterval = null, int checkpointIntervalCurrent = 0) {
		if (!_subscriptionTopics.TryGetValue(eventStreamId, out var subscribers)) {
			subscribers = [];
			_subscriptionTopics.Add(eventStreamId, subscribers);
		}

		// if eventStreamId is null or empty -- subscription is to all streams
		var subscription = new Subscription(correlationId,
			envelope,
			connectionId,
			eventStreamId.IsEmptyString() ? AllStreamsSubscriptionId : eventStreamId,
			resolveLinkTos,
			lastIndexedPosition,
			lastEventNumber ?? -1,
			user,
			eventFilter,
			checkpointInterval,
			checkpointIntervalCurrent);
		subscribers.Add(subscription);
		_subscriptionsById[correlationId] = subscription;
	}

	private void DropSubscription(Guid subscriptionId, SubscriptionDropReason dropReason) {
		if (_subscriptionsById.TryGetValue(subscriptionId, out var subscription))
			DropSubscription(subscription, dropReason, sendDropNotification: true);
	}

	private void DropSubscription(Subscription subscription, SubscriptionDropReason dropReason, bool sendDropNotification) {
		if (sendDropNotification)
			subscription.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(subscription.CorrelationId, dropReason));

		if (_subscriptionTopics.TryGetValue(subscription.EventStreamId, out var subscriptions)) {
			subscriptions.Remove(subscription);
			if (subscriptions.Count == 0)
				_subscriptionTopics.Remove(subscription.EventStreamId);
		}

		_subscriptionsById.Remove(subscription.CorrelationId);
	}

	/* LONG POLL SECTION */
	public void Handle(SubscriptionMessage.PollStream message) {
		if (MissedEvents(message.StreamId, message.LastIndexedPosition, message.LastEventNumber)) {
			_bus.Publish(CloneReadRequestWithNoPollFlag(message.OriginalRequest));
			return;
		}

		SubscribePoller(message.StreamId, message.ExpireAt, message.LastIndexedPosition, message.LastEventNumber, message.OriginalRequest);
	}

	private bool MissedEvents(string streamId, long lastIndexedPosition, long? lastEventNumber) {
		return SystemStreams.IsVirtualStream(streamId)
			? _lastSeenInMemoryCommitPosition > lastIndexedPosition
			: _lastSeenCommitPosition > lastIndexedPosition;
	}

	private void SubscribePoller(string streamId, DateTime expireAt, long lastIndexedPosition, long? lastEventNumber, Message originalRequest) {
		if (!_pollTopics.TryGetValue(streamId, out var pollTopic)) {
			pollTopic = [];
			_pollTopics.Add(streamId, pollTopic);
		}

		pollTopic.Add(new(expireAt, lastIndexedPosition, lastEventNumber ?? -1, originalRequest));
	}

	public void Handle(SubscriptionMessage.CheckPollTimeout message) {
		List<string> pollTopicsToRemove = null;
		var now = DateTime.UtcNow;
		foreach (var (key, pollTopic) in _pollTopics) {
			for (int i = pollTopic.Count - 1; i >= 0; --i) {
				var poller = pollTopic[i];
				if (poller.ExpireAt > now)
					continue;

				_bus.Publish(CloneReadRequestWithNoPollFlag(poller.OriginalRequest));
				pollTopic.RemoveAt(i);

				if (pollTopic.Count == 0) // schedule removal of list instance
				{
					pollTopicsToRemove ??= [];
					pollTopicsToRemove.Add(key);
				}
			}
		}

		if (pollTopicsToRemove != null) {
			for (int i = 0, n = pollTopicsToRemove.Count; i < n; ++i) {
				_pollTopics.Remove(pollTopicsToRemove[i]);
			}
		}

		_bus.Publish(TimerMessage.Schedule.Create(TimeSpan.FromSeconds(1), _busEnvelope, message));
	}

	private static Message CloneReadRequestWithNoPollFlag(Message originalRequest) {
		return originalRequest switch {
			ClientMessage.ReadStreamEventsForward streamReq => new ClientMessage.ReadStreamEventsForward(streamReq.InternalCorrId, streamReq.CorrelationId, streamReq.Envelope, streamReq.EventStreamId,
				streamReq.FromEventNumber, streamReq.MaxCount, streamReq.ResolveLinkTos, streamReq.RequireLeader, streamReq.ValidationStreamVersion, streamReq.User,
				replyOnExpired: streamReq.ReplyOnExpired),
			ClientMessage.ReadAllEventsForward allReq => new ClientMessage.ReadAllEventsForward(allReq.InternalCorrId, allReq.CorrelationId, allReq.Envelope, allReq.CommitPosition,
				allReq.PreparePosition, allReq.MaxCount, allReq.ResolveLinkTos, allReq.RequireLeader, allReq.ValidationTfLastCommitPosition, allReq.User, replyOnExpired: allReq.ReplyOnExpired),
			_ => throw new Exception($"Unexpected read request of type {originalRequest.GetType()} for long polling: {originalRequest}.")
		};
	}

	async ValueTask IAsyncHandle<StorageMessage.EventCommitted>.HandleAsync(StorageMessage.EventCommitted message, CancellationToken token) {
		_lastSeenCommitPosition = message.CommitPosition;

		var resolvedEvent =
			await ProcessEventCommitted(AllStreamsSubscriptionId, message.CommitPosition, message.Event, null, token);
		await ProcessEventCommitted(message.Event.EventStreamId, message.CommitPosition, message.Event, resolvedEvent, token);

		ProcessStreamMetadataChanges(message.Event.EventStreamId);
		ProcessSettingsStreamChanges(message.Event.EventStreamId);

		ReissueReadsFor(AllStreamsSubscriptionId, message.CommitPosition, message.Event.EventNumber);
		ReissueReadsFor(message.Event.EventStreamId, message.CommitPosition, message.Event.EventNumber);
	}

	async ValueTask IAsyncHandle<StorageMessage.InMemoryEventCommitted>.HandleAsync(StorageMessage.InMemoryEventCommitted message, CancellationToken token) {
		_lastSeenInMemoryCommitPosition = message.CommitPosition;
		await ProcessEventCommitted(message.Event.EventStreamId, message.CommitPosition, message.Event, null, token);
		ProcessStreamMetadataChanges(message.Event.EventStreamId);
		ProcessSettingsStreamChanges(message.Event.EventStreamId);
		ReissueReadsFor(message.Event.EventStreamId, message.CommitPosition, message.Event.EventNumber);
	}

	private async ValueTask<ResolvedEvent?> ProcessEventCommitted(string eventStreamId, long commitPosition, EventRecord evnt, ResolvedEvent? resolvedEvent, CancellationToken token) {
		if (!_subscriptionTopics.TryGetValue(eventStreamId, out var subscriptions))
			return resolvedEvent;
		for (int i = 0, n = subscriptions.Count; i < n; i++) {
			var subscr = subscriptions[i];
			if (commitPosition <= subscr.LastIndexedPosition || evnt.EventNumber <= subscr.LastEventNumber)
				continue;

			var pair = ResolvedEvent.ForUnresolvedEvent(evnt, commitPosition);
			if (subscr.ResolveLinkTos)
				// resolve event if has not been previously resolved
				resolvedEvent = pair = resolvedEvent ?? await ResolveLinkToEvent(evnt, commitPosition, token);

			if (subscr.EventFilter.IsEventAllowed(evnt)) {
				subscr.Envelope.ReplyWith(new ClientMessage.StreamEventAppeared(subscr.CorrelationId, pair));
			}

			if (subscr.CheckpointInterval == DontReportCheckpointReached)
				continue;

			subscr.CheckpointIntervalCurrent++;

			if (subscr.CheckpointInterval != null &&
				subscr.CheckpointIntervalCurrent >= subscr.CheckpointInterval) {
				subscr.Envelope.ReplyWith(new ClientMessage.CheckpointReached(subscr.CorrelationId, pair.OriginalPosition));
				subscr.CheckpointIntervalCurrent = 0;
			}
		}

		return resolvedEvent;
	}

	private void ProcessStreamMetadataChanges(string eventStreamId) {
		if (!SystemStreams.IsMetastream(eventStreamId))
			return;

		eventStreamId = SystemStreams.OriginalStreamOf(eventStreamId);

		if (eventStreamId == SystemStreams.AllStream)
			eventStreamId = string.Empty;

		if (!_subscriptionTopics.TryGetValue(eventStreamId, out var subscriptions))
			return;

		foreach (var subscription in subscriptions.ToArray())
			Authorize(subscription);
	}

	private void ProcessSettingsStreamChanges(string eventStreamId) {
		if (eventStreamId != SystemStreams.SettingsStream)
			return;

		foreach (var subscriptions in _subscriptionTopics.Values) {
			foreach (var subscription in subscriptions.ToArray())
				Authorize(subscription);
		}
	}

	private void Authorize(Subscription subscription) {
		try {
			var streamId = Operations.Streams.Parameters.StreamId(subscription.EventStreamId);
			var op = new Operation(Operations.Streams.Read).WithParameter(streamId);

			var accessChk = _authorizationProvider.CheckAccessAsync(subscription.User, op, CancellationToken.None);

			if (accessChk.IsCompleted)
				AuthorizeSync();
			else
				_ = AuthorizeAsync();

			void AuthorizeSync() {
				if (accessChk.Result)
					return;

				LogSubscriptionDrop();
				DropSubscription(subscription, SubscriptionDropReason.AccessDenied, sendDropNotification: true);
			}

			async Task AuthorizeAsync() {
				// note: when authorizing asynchronously, a few live events may go through before the "Access Denied" message is sent to the subscription
				if (await accessChk)
					return;

				LogSubscriptionDrop();
				// we go through the queue to avoid the need for any lock
				_bus.Publish(new SubscriptionMessage.DropSubscription(subscription.CorrelationId, SubscriptionDropReason.AccessDenied));
			}
		} catch (Exception ex) {
			LogException(ex);
		}

		void LogSubscriptionDrop() {
			Log.Debug(
				"Dropping live subscription to stream: {streamId} (Connection ID: {connectionId}) following new stream metadata.",
				subscription.EventStreamId, subscription.ConnectionId);
		}

		void LogException(Exception ex) {
			Log.Error(ex, "Failed to check access for live subscription to stream: {streamId} (Connection ID: {connectionId}) following new stream metadata. Live subscription will continue to run.",
				subscription.EventStreamId, subscription.ConnectionId);
		}
	}

	private async ValueTask<ResolvedEvent> ResolveLinkToEvent(EventRecord eventRecord, long commitPosition, CancellationToken token) {
		if (eventRecord.EventType is SystemEventTypes.LinkTo) {
			try {
				string[] parts = Helper.UTF8NoBom.GetString(eventRecord.Data.Span).Split(LinkToSeparator, 2);
				long eventNumber = long.Parse(parts[0]);
				string streamName = parts[1];
				var streamId = _readIndex.GetStreamId(streamName);
				var res = await _readIndex.ReadEvent(streamName, streamId, eventNumber, token);

				return res.Result is ReadEventResult.Success
					? ResolvedEvent.ForResolvedLink(res.Record, eventRecord, commitPosition)
					: ResolvedEvent.ForFailedResolvedLink(eventRecord, res.Result, commitPosition);
			} catch (Exception exc) {
				Log.Error(exc, "Error while resolving link for event record: {eventRecord}",
					eventRecord.ToString());
			}

			// return unresolved link
			return ResolvedEvent.ForFailedResolvedLink(eventRecord, ReadEventResult.Error, commitPosition);
		}

		return ResolvedEvent.ForUnresolvedEvent(eventRecord, commitPosition);
	}

	private void ReissueReadsFor(string streamId, long commitPosition, long eventNumber) {
		if (!_pollTopics.TryGetValue(streamId, out var pollTopic))
			return;

		List<PollSubscription> survivors = null;
		foreach (var poller in pollTopic) {
			if (commitPosition <= poller.LastIndexedPosition || eventNumber <= poller.LastEventNumber) {
				survivors ??= [];
				survivors.Add(poller);
			} else {
				_bus.Publish(CloneReadRequestWithNoPollFlag(poller.OriginalRequest));
			}
		}

		_pollTopics.Remove(streamId);
		if (survivors != null)
			_pollTopics.Add(streamId, survivors);
	}

	private class Subscription(
		Guid correlationId,
		IEnvelope envelope,
		Guid connectionId,
		string eventStreamId,
		bool resolveLinkTos,
		long lastIndexedPosition,
		long lastEventNumber,
		ClaimsPrincipal user,
		IEventFilter eventFilter,
		int? checkpointInterval,
		int checkpointIntervalCurrent) {
		public readonly Guid CorrelationId = correlationId;
		public readonly IEnvelope Envelope = envelope;
		public readonly Guid ConnectionId = connectionId;
		public readonly string EventStreamId = eventStreamId;
		public readonly bool ResolveLinkTos = resolveLinkTos;
		public readonly long LastIndexedPosition = lastIndexedPosition;
		public readonly long LastEventNumber = lastEventNumber;
		public readonly ClaimsPrincipal User = user;
		public readonly IEventFilter EventFilter = eventFilter;
		public readonly int? CheckpointInterval = checkpointInterval;
		public int CheckpointIntervalCurrent = checkpointInterval == null ? 0 : checkpointIntervalCurrent;
	}

	private class PollSubscription(
		DateTime expireAt,
		long lastIndexedPosition,
		long lastEventNumber,
		Message originalRequest) {
		public readonly DateTime ExpireAt = expireAt;
		public readonly long LastIndexedPosition = lastIndexedPosition;
		public readonly long LastEventNumber = lastEventNumber;
		public readonly Message OriginalRequest = originalRequest;
	}
}
