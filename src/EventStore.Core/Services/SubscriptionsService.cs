using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.TimerService;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services {
	public enum SubscriptionDropReason {
		Unsubscribed = 0,
		AccessDenied = 1,
		NotFound = 2,
		PersistentSubscriptionDeleted = 3,
		SubscriberMaxCountReached = 4
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
		IHandle<ClientMessage.SubscribeToStream>,
		IHandle<ClientMessage.FilteredSubscribeToStream>,
		IHandle<ClientMessage.UnsubscribeFromStream>,
		IHandle<SubscriptionMessage.PollStream>,
		IHandle<SubscriptionMessage.CheckPollTimeout>,
		IHandle<StorageMessage.InMemoryEventCommitted>,
		IHandle<StorageMessage.EventCommitted> {
		private const int DontReportCheckpointReached = -1;

		private static readonly TimeSpan TimeoutPeriod = TimeSpan.FromSeconds(1);

		private readonly Dictionary<string, List<Subscription>> _subscriptionTopics =
			new Dictionary<string, List<Subscription>>();

		private readonly Dictionary<Guid, Subscription> _subscriptionsById = new Dictionary<Guid, Subscription>();

		private readonly Dictionary<string, List<PollSubscription>> _pollTopics =
			new Dictionary<string, List<PollSubscription>>();

		private long _lastSeenCommitPosition = -1;
		private long _lastSeenInMemoryCommitPosition = -1;

		private readonly IPublisher _bus;
		private readonly IEnvelope _busEnvelope;
		private readonly IQueuedHandler _queuedHandler;
		private readonly IReadIndex<TStreamId> _readIndex;
		private static readonly char[] _linkToSeparator = new[] { '@' };

		public SubscriptionsService(
			IPublisher bus,
			IQueuedHandler queuedHandler,
			IReadIndex<TStreamId> readIndex) {

			Ensure.NotNull(bus, "bus");
			Ensure.NotNull(queuedHandler, "queuedHandler");
			Ensure.NotNull(readIndex, "readIndex");

			_bus = bus;
			_busEnvelope = new PublishEnvelope(bus);
			_queuedHandler = queuedHandler;
			_readIndex = readIndex;
		}

		public void Handle(SystemMessage.SystemStart message) {
			_bus.Publish(TimerMessage.Schedule.Create(TimeoutPeriod, _busEnvelope,
				new SubscriptionMessage.CheckPollTimeout()));
		}

		/* SUBSCRIPTION SECTION */
		public void Handle(SystemMessage.BecomeShuttingDown message) {
			List<Subscription> subscriptions = _subscriptionsById.Values.ToList();
			foreach (var subscription in subscriptions) {
				DropSubscription(subscription, sendDropNotification: true);
			}

			_queuedHandler.RequestStop();
		}

		public void Handle(TcpMessage.ConnectionClosed message) {
			List<string> subscriptionGroupsToRemove = null;
			foreach (var subscriptionGroup in _subscriptionTopics) {
				var subscriptions = subscriptionGroup.Value;
				for (int i = 0, n = subscriptions.Count; i < n; ++i) {
					if (subscriptions[i].ConnectionId == message.Connection.ConnectionId)
						_subscriptionsById.Remove(subscriptions[i].CorrelationId);
				}

				subscriptions.RemoveAll(x => x.ConnectionId == message.Connection.ConnectionId);
				if (subscriptions.Count == 0) // schedule removal of list instance
				{
					if (subscriptionGroupsToRemove == null)
						subscriptionGroupsToRemove = new List<string>();
					subscriptionGroupsToRemove.Add(subscriptionGroup.Key);
				}
			}

			if (subscriptionGroupsToRemove != null) {
				for (int i = 0, n = subscriptionGroupsToRemove.Count; i < n; ++i) {
					_subscriptionTopics.Remove(subscriptionGroupsToRemove[i]);
				}
			}
		}

		public void Handle(ClientMessage.SubscribeToStream msg) {
			var isInMemoryStream = SystemStreams.IsInMemoryStream(msg.EventStreamId);

			long? lastEventNumber = null;
			if (isInMemoryStream) {
				lastEventNumber = -1;
			} else if (!msg.EventStreamId.IsEmptyString()) {
				lastEventNumber = _readIndex.GetStreamLastEventNumber(_readIndex.GetStreamId(msg.EventStreamId));
			}

			var lastIndexedPos = isInMemoryStream ? -1 : _readIndex.LastIndexedPosition;

			SubscribeToStream(msg.CorrelationId, msg.Envelope, msg.ConnectionId, msg.EventStreamId,
				msg.ResolveLinkTos, lastIndexedPos, lastEventNumber,
				msg.EventStreamId.IsEmptyString() ? EventFilter.DefaultAllFilter : EventFilter.DefaultStreamFilter);
			var subscribedMessage =
				new ClientMessage.SubscriptionConfirmation(msg.CorrelationId, lastIndexedPos, lastEventNumber);
			msg.Envelope.ReplyWith(subscribedMessage);
		}

		public void Handle(ClientMessage.FilteredSubscribeToStream msg) {
			var isInMemoryStream = SystemStreams.IsInMemoryStream(msg.EventStreamId);

			long? lastEventNumber = null;
			if (isInMemoryStream) {
				lastEventNumber = -1;
			} else if (!msg.EventStreamId.IsEmptyString()) {
				lastEventNumber = _readIndex.GetStreamLastEventNumber(_readIndex.GetStreamId(msg.EventStreamId));
			}

			var lastIndexedPos = isInMemoryStream ? -1 : _readIndex.LastIndexedPosition;

			SubscribeToStream(msg.CorrelationId, msg.Envelope, msg.ConnectionId, msg.EventStreamId,
				msg.ResolveLinkTos, lastIndexedPos, lastEventNumber, msg.EventFilter,
				msg.CheckpointInterval, msg.CheckpointIntervalCurrent);
			var subscribedMessage =
				new ClientMessage.SubscriptionConfirmation(msg.CorrelationId, lastIndexedPos, lastEventNumber);
			msg.Envelope.ReplyWith(subscribedMessage);

		}

		public void Handle(ClientMessage.UnsubscribeFromStream message) {
			UnsubscribeFromStream(message.CorrelationId);
		}

		private void SubscribeToStream(Guid correlationId, IEnvelope envelope, Guid connectionId,
			string eventStreamId, bool resolveLinkTos, long lastIndexedPosition, long? lastEventNumber,
			IEventFilter eventFilter, int? checkpointInterval = null, int checkpointIntervalCurrent = 0) {
			List<Subscription> subscribers;
			if (!_subscriptionTopics.TryGetValue(eventStreamId, out subscribers)) {
				subscribers = new List<Subscription>();
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
				eventFilter,
				checkpointInterval,
				checkpointIntervalCurrent);
			subscribers.Add(subscription);
			_subscriptionsById[correlationId] = subscription;
		}

		private void UnsubscribeFromStream(Guid correlationId) {
			Subscription subscription;
			if (_subscriptionsById.TryGetValue(correlationId, out subscription))
				DropSubscription(subscription, sendDropNotification: true);
		}

		private void DropSubscription(Subscription subscription, bool sendDropNotification) {
			if (sendDropNotification)
				subscription.Envelope.ReplyWith(
					new ClientMessage.SubscriptionDropped(subscription.CorrelationId,
						SubscriptionDropReason.Unsubscribed));

			List<Subscription> subscriptions;
			if (_subscriptionTopics.TryGetValue(subscription.EventStreamId, out subscriptions)) {
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

			SubscribePoller(message.StreamId, message.ExpireAt, message.LastIndexedPosition, message.LastEventNumber,
				message.OriginalRequest);
		}

		private bool MissedEvents(string streamId, long lastIndexedPosition, long? lastEventNumber) {
			return SystemStreams.IsInMemoryStream(streamId)
				? _lastSeenInMemoryCommitPosition > lastIndexedPosition
				: _lastSeenCommitPosition > lastIndexedPosition;
		}

		private void SubscribePoller(string streamId, DateTime expireAt, long lastIndexedPosition, long? lastEventNumber,
			Message originalRequest) {
			List<PollSubscription> pollTopic;
			if (!_pollTopics.TryGetValue(streamId, out pollTopic)) {
				pollTopic = new List<PollSubscription>();
				_pollTopics.Add(streamId, pollTopic);
			}

			pollTopic.Add(new PollSubscription(expireAt, lastIndexedPosition, lastEventNumber ?? -1, originalRequest));
		}

		public void Handle(SubscriptionMessage.CheckPollTimeout message) {
			List<string> pollTopicsToRemove = null;
			var now = DateTime.UtcNow;
			foreach (var pollTopicKeyVal in _pollTopics) {
				var pollTopic = pollTopicKeyVal.Value;
				for (int i = pollTopic.Count - 1; i >= 0; --i) {
					var poller = pollTopic[i];
					if (poller.ExpireAt <= now) {
						_bus.Publish(CloneReadRequestWithNoPollFlag(poller.OriginalRequest));
						pollTopic.RemoveAt(i);

						if (pollTopic.Count == 0) // schedule removal of list instance
						{
							if (pollTopicsToRemove == null)
								pollTopicsToRemove = new List<string>();
							pollTopicsToRemove.Add(pollTopicKeyVal.Key);
						}
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

		private Message CloneReadRequestWithNoPollFlag(Message originalRequest) {
			var streamReq = originalRequest as ClientMessage.ReadStreamEventsForward;
			if (streamReq != null)
				return new ClientMessage.ReadStreamEventsForward(
					streamReq.InternalCorrId, streamReq.CorrelationId, streamReq.Envelope,
					streamReq.EventStreamId, streamReq.FromEventNumber, streamReq.MaxCount, streamReq.ResolveLinkTos,
					streamReq.RequireLeader, streamReq.ValidationStreamVersion, streamReq.User,
					replyOnExpired: streamReq.ReplyOnExpired);

			var allReq = originalRequest as ClientMessage.ReadAllEventsForward;
			if (allReq != null)
				return new ClientMessage.ReadAllEventsForward(
					allReq.InternalCorrId, allReq.CorrelationId, allReq.Envelope,
					allReq.CommitPosition, allReq.PreparePosition, allReq.MaxCount, allReq.ResolveLinkTos,
					allReq.RequireLeader, allReq.ValidationTfLastCommitPosition, allReq.User,
					replyOnExpired: allReq.ReplyOnExpired);

			throw new Exception(string.Format("Unexpected read request of type {0} for long polling: {1}.",
				originalRequest.GetType(), originalRequest));
		}

		public void Handle(StorageMessage.EventCommitted message) {
			_lastSeenCommitPosition = message.CommitPosition;

			var resolvedEvent =
				ProcessEventCommited(AllStreamsSubscriptionId, message.CommitPosition, message.Event, null);
			ProcessEventCommited(message.Event.EventStreamId, message.CommitPosition, message.Event, resolvedEvent);

			ReissueReadsFor(AllStreamsSubscriptionId, message.CommitPosition, message.Event.EventNumber);
			ReissueReadsFor(message.Event.EventStreamId, message.CommitPosition, message.Event.EventNumber);
		}

		public void Handle(StorageMessage.InMemoryEventCommitted message) {
			_lastSeenInMemoryCommitPosition = message.CommitPosition;
			ProcessEventCommited(message.Event.EventStreamId, message.CommitPosition, message.Event, null);
			ReissueReadsFor(message.Event.EventStreamId, message.CommitPosition, message.Event.EventNumber);
		}

		private ResolvedEvent? ProcessEventCommited(
			string eventStreamId, long commitPosition, EventRecord evnt, ResolvedEvent? resolvedEvent) {
			List<Subscription> subscriptions;
			if (!_subscriptionTopics.TryGetValue(eventStreamId, out subscriptions))
				return resolvedEvent;
			for (int i = 0, n = subscriptions.Count; i < n; i++) {
				var subscr = subscriptions[i];
				if (commitPosition <= subscr.LastIndexedPosition || evnt.EventNumber <= subscr.LastEventNumber)
					continue;

				var pair = ResolvedEvent.ForUnresolvedEvent(evnt, commitPosition);
				if (subscr.ResolveLinkTos)
					// resolve event if has not been previously resolved
					resolvedEvent = pair = resolvedEvent ?? ResolveLinkToEvent(evnt, commitPosition);

				if (subscr.EventFilter.IsEventAllowed(evnt)) {
					subscr.Envelope.ReplyWith(new ClientMessage.StreamEventAppeared(subscr.CorrelationId, pair));
				}

				if (subscr.CheckpointInterval == DontReportCheckpointReached)
					continue;

				subscr.CheckpointIntervalCurrent++;

				if (subscr.CheckpointInterval != null &&
					subscr.CheckpointIntervalCurrent >= subscr.CheckpointInterval) {
					subscr.Envelope.ReplyWith(new ClientMessage.CheckpointReached(subscr.CorrelationId,
						pair.OriginalPosition));
					subscr.CheckpointIntervalCurrent = 0;
				}
			}

			return resolvedEvent;
		}

		private ResolvedEvent ResolveLinkToEvent(EventRecord eventRecord, long commitPosition) {
			if (eventRecord.EventType == SystemEventTypes.LinkTo) {
				try {
					string[] parts = Helper.UTF8NoBom.GetString(eventRecord.Data.Span).Split(_linkToSeparator, 2);
					long eventNumber = long.Parse(parts[0]);
					string streamName = parts[1];
					var streamId = _readIndex.GetStreamId(streamName);
					var res = _readIndex.ReadEvent(streamName, streamId, eventNumber);

					if (res.Result == ReadEventResult.Success)
						return ResolvedEvent.ForResolvedLink(res.Record, eventRecord, commitPosition);

					return ResolvedEvent.ForFailedResolvedLink(eventRecord, res.Result, commitPosition);
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
			List<PollSubscription> pollTopic;
			if (_pollTopics.TryGetValue(streamId, out pollTopic)) {
				List<PollSubscription> survivors = null;
				foreach (var poller in pollTopic) {
					if (commitPosition <= poller.LastIndexedPosition || eventNumber <= poller.LastEventNumber) {
						if (survivors == null)
							survivors = new List<PollSubscription>();
						survivors.Add(poller);
					} else {
						_bus.Publish(CloneReadRequestWithNoPollFlag(poller.OriginalRequest));
					}
				}

				_pollTopics.Remove(streamId);
				if (survivors != null)
					_pollTopics.Add(streamId, survivors);
			}
		}

		private class Subscription {
			public readonly Guid CorrelationId;
			public readonly IEnvelope Envelope;
			public readonly Guid ConnectionId;

			public readonly string EventStreamId;
			public readonly bool ResolveLinkTos;
			public readonly long LastIndexedPosition;
			public readonly long LastEventNumber;
			public readonly IEventFilter EventFilter;
			public readonly int? CheckpointInterval;

			public int CheckpointIntervalCurrent = 0;

			public Subscription(Guid correlationId,
				IEnvelope envelope,
				Guid connectionId,
				string eventStreamId,
				bool resolveLinkTos,
				long lastIndexedPosition,
				long lastEventNumber,
				IEventFilter eventFilter,
				int? checkpointInterval,
				int checkpointIntervalCurrent) {
				CorrelationId = correlationId;
				Envelope = envelope;
				ConnectionId = connectionId;

				EventStreamId = eventStreamId;
				ResolveLinkTos = resolveLinkTos;
				LastIndexedPosition = lastIndexedPosition;
				LastEventNumber = lastEventNumber;

				EventFilter = eventFilter;
				CheckpointInterval = checkpointInterval;
				CheckpointIntervalCurrent = checkpointInterval == null ? 0 : checkpointIntervalCurrent;
			}
		}

		private class PollSubscription {
			public readonly DateTime ExpireAt;
			public readonly long LastIndexedPosition;
			public readonly long LastEventNumber;

			public readonly Message OriginalRequest;

			public PollSubscription(DateTime expireAt, long lastIndexedPosition, long lastEventNumber,
				Message originalRequest) {
				ExpireAt = expireAt;
				LastIndexedPosition = lastIndexedPosition;
				LastEventNumber = lastEventNumber;
				OriginalRequest = originalRequest;
			}
		}
	}
}
