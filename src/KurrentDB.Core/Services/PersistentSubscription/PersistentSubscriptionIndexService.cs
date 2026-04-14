// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.PersistentSubscription;

public class PersistentSubscriptionIndexService :
	IHandle<SubscriptionMessage.PersistentSubscriptionIndexEntriesLoaded>,
	IHandle<SubscriptionMessage.PersistentSubscriptionTimerTick>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<ClientMessage.CreatePersistentSubscriptionToIndex>,
	IHandle<ClientMessage.UpdatePersistentSubscriptionToIndex>,
	IHandle<ClientMessage.DeletePersistentSubscriptionToIndex>,
	IAsyncHandle<ClientMessage.ConnectToPersistentSubscriptionToIndex>,
	IAsyncHandle<StorageMessage.SecondaryIndexCommitted>,
	IAsyncHandle<StorageMessage.SecondaryIndexDeleted> {

	private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscriptionIndexService>();

	private readonly Dictionary<string, PersistentSubscription> _subscriptionsById = new();
	private readonly Dictionary<string, List<PersistentSubscription>> _subscriptionTopics = new();

	private readonly IQueuedHandler _queuedHandler;
	private readonly IODispatcher _ioDispatcher;
	private readonly IPublisher _mainQueue;
	private readonly PersistentSubscriptionConsumerStrategyRegistry _consumerStrategyRegistry;
	private readonly SecondaryIndexReaders _secondaryIndexReaders;
	private readonly IPersistentSubscriptionStreamReader _streamReader;
	private readonly IPersistentSubscriptionCheckpointReader _checkpointReader;
	private PersistentSubscriptionConfig _config = new() { Version = "2" };

	public PersistentSubscriptionIndexService(
		IQueuedHandler queuedHandler,
		IODispatcher ioDispatcher,
		IPublisher mainQueue,
		PersistentSubscriptionConsumerStrategyRegistry consumerStrategyRegistry,
		SecondaryIndexReaders secondaryIndexReaders) {
		Ensure.NotNull(queuedHandler, "queuedHandler");
		Ensure.NotNull(ioDispatcher, "ioDispatcher");
		Ensure.NotNull(mainQueue, "mainQueue");
		Ensure.NotNull(consumerStrategyRegistry, "consumerStrategyRegistry");
		Ensure.NotNull(secondaryIndexReaders, "secondaryIndexReaders");

		_queuedHandler = queuedHandler;
		_ioDispatcher = ioDispatcher;
		_mainQueue = mainQueue;
		_consumerStrategyRegistry = consumerStrategyRegistry;
		_secondaryIndexReaders = secondaryIndexReaders;
		_streamReader = new PersistentSubscriptionStreamReader(ioDispatcher, mainQueue, maxPullBatchSize: 100);
		_checkpointReader = new PersistentSubscriptionCheckpointReader(ioDispatcher);
	}

	// Bootstrap: entries forwarded from the main PersistentSubscriptionService on config load.
	public void Handle(SubscriptionMessage.PersistentSubscriptionIndexEntriesLoaded message) {
		foreach (var entry in message.IndexEntries) {
			var indexName = entry.IndexName;
			if (indexName is null) {
				Log.Warning("Index entry with null IndexName encountered during bootstrap. Skipping.");
				continue;
			}

			if (!_secondaryIndexReaders.CanReadIndex(indexName)) {
				Log.Warning("Index '{indexName}' is not available. Skipping persistent subscription group '{group}'.",
					indexName, entry.Group);
				continue;
			}

			if (!_consumerStrategyRegistry.ValidateStrategy(entry.NamedConsumerStrategy)) {
				Log.Error(
					"A persistent subscription to index '{indexName}' exists with an invalid consumer strategy '{strategy}'. Ignoring it.",
					indexName, entry.NamedConsumerStrategy);
				continue;
			}

			var eventSource = new PersistentSubscriptionIndexEventSource(indexName);
#pragma warning disable 612
			var startFrom = eventSource.GetStreamPositionFor(entry.StartPosition ?? entry.StartFrom.ToString());
#pragma warning restore 612

			var result = TryCreateSubscriptionGroup(
				eventSource,
				entry.Group,
				entry.ResolveLinkTos,
				startFrom,
				entry.ExtraStatistics,
				entry.MaxRetryCount,
				entry.LiveBufferSize,
				entry.HistoryBufferSize,
				entry.ReadBatchSize,
				ToCheckPointAfterTimeout(entry.CheckPointAfter),
				entry.MinCheckPointCount,
				entry.MaxCheckPointCount,
				entry.MaxSubscriberCount,
				entry.NamedConsumerStrategy,
				ToMessageTimeout(entry.MessageTimeout));

			if (!result) {
				var key = BuildSubscriptionGroupKey(eventSource.ToString(), entry.Group);
				Log.Warning(
					"A duplicate persistent subscription to index: {subscriptionKey} was found in the configuration. Ignoring it.",
					key);
			}
		}
	}

	public void Handle(SubscriptionMessage.PersistentSubscriptionTimerTick message) {
		var now = DateTime.UtcNow;
		foreach (var subscription in _subscriptionsById.Values) {
			subscription.NotifyClockTick(now);
		}
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		ShutdownSubscriptions();
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		if (message.State == VNodeState.Leader)
			return;

		Log.Debug("Persistent index subscriptions received state change to {state}. Shutting down subscriptions.", message.State);
		ShutdownSubscriptions();
	}

	private void ShutdownSubscriptions() {
		foreach (var subscription in _subscriptionsById.Values) {
			subscription.Shutdown();
		}
	}

	public void Handle(ClientMessage.CreatePersistentSubscriptionToIndex message) {
		var indexName = message.IndexName;

		if (!_secondaryIndexReaders.CanReadIndex(indexName)) {
			message.Envelope.ReplyWith(new ClientMessage.CreatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Fail,
				$"Index '{indexName}' does not exist or is not available."));
			return;
		}

		if (!_consumerStrategyRegistry.ValidateStrategy(message.NamedConsumerStrategy)) {
			message.Envelope.ReplyWith(new ClientMessage.CreatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Fail,
				$"Consumer strategy {message.NamedConsumerStrategy} does not exist."));
			return;
		}

		var eventSource = new PersistentSubscriptionIndexEventSource(indexName);
		var stream = eventSource.ToString();
		var key = BuildSubscriptionGroupKey(stream, message.GroupName);
		Log.Debug("Creating persistent subscription to index {subscriptionKey}", key);

		var startFrom = new PersistentSubscriptionAllStreamPosition(
			message.StartFrom.CommitPosition, message.StartFrom.PreparePosition);

		var result = TryCreateSubscriptionGroup(
			eventSource,
			message.GroupName,
			message.ResolveLinkTos,
			startFrom,
			message.RecordStatistics,
			message.MaxRetryCount,
			message.LiveBufferSize,
			message.BufferSize,
			message.ReadBatchSize,
			ToCheckPointAfterTimeout(message.CheckPointAfterMilliseconds),
			message.MinCheckPointCount,
			message.MaxCheckPointCount,
			message.MaxSubscriberCount,
			message.NamedConsumerStrategy,
			ToMessageTimeout(message.MessageTimeoutMilliseconds));

		if (!result) {
			message.Envelope.ReplyWith(new ClientMessage.CreatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.AlreadyExists,
				$"Group '{message.GroupName}' already exists."));
			return;
		}

		var createEntry = new PersistentSubscriptionEntry {
			Stream = stream,
			IndexName = indexName,
			Group = message.GroupName,
			ResolveLinkTos = message.ResolveLinkTos,
			CheckPointAfter = message.CheckPointAfterMilliseconds,
			ExtraStatistics = message.RecordStatistics,
			HistoryBufferSize = message.BufferSize,
			LiveBufferSize = message.LiveBufferSize,
			MaxCheckPointCount = message.MaxCheckPointCount,
			MinCheckPointCount = message.MinCheckPointCount,
			MaxRetryCount = message.MaxRetryCount,
			ReadBatchSize = message.ReadBatchSize,
			MaxSubscriberCount = message.MaxSubscriberCount,
			MessageTimeout = message.MessageTimeoutMilliseconds,
			NamedConsumerStrategy = message.NamedConsumerStrategy,
#pragma warning disable 612
			StartFrom = long.MinValue,
#pragma warning restore 612
			StartPosition = startFrom.ToString()
		};

		UpdateSubscriptionConfig(message.User?.Identity?.Name, stream, message.GroupName, createEntry);
		SaveConfiguration(() => message.Envelope.ReplyWith(
			new ClientMessage.CreatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
				"")));
	}

	public void Handle(ClientMessage.UpdatePersistentSubscriptionToIndex message) {
		var indexName = message.IndexName;
		var eventSource = new PersistentSubscriptionIndexEventSource(indexName);
		var stream = eventSource.ToString();
		var key = BuildSubscriptionGroupKey(stream, message.GroupName);
		Log.Debug("Updating persistent subscription to index {subscriptionKey}", key);

		if (!_subscriptionsById.TryGetValue(key, out _)) {
			message.Envelope.ReplyWith(new ClientMessage.UpdatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.UpdatePersistentSubscriptionToIndexCompleted.UpdatePersistentSubscriptionToIndexResult.DoesNotExist,
				$"Group '{message.GroupName}' does not exist."));
			return;
		}

		if (!_consumerStrategyRegistry.ValidateStrategy(message.NamedConsumerStrategy)) {
			message.Envelope.ReplyWith(new ClientMessage.UpdatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.UpdatePersistentSubscriptionToIndexCompleted.UpdatePersistentSubscriptionToIndexResult.Fail,
				$"Consumer strategy {message.NamedConsumerStrategy} does not exist."));
			return;
		}

		var startFrom = new PersistentSubscriptionAllStreamPosition(
			message.StartFrom.CommitPosition, message.StartFrom.PreparePosition);

		var subscription = new PersistentSubscription(
			new PersistentSubscriptionParams(
				message.ResolveLinkTos,
				key,
				eventSource,
				message.GroupName,
				startFrom,
				message.RecordStatistics,
				ToMessageTimeout(message.MessageTimeoutMilliseconds),
				message.MaxRetryCount,
				message.LiveBufferSize,
				message.BufferSize,
				message.ReadBatchSize,
				ToCheckPointAfterTimeout(message.CheckPointAfterMilliseconds),
				message.MinCheckPointCount,
				message.MaxCheckPointCount,
				message.MaxSubscriberCount,
				_consumerStrategyRegistry.GetInstance(message.NamedConsumerStrategy, key),
				_streamReader,
				_checkpointReader,
				new PersistentSubscriptionCheckpointWriter(key, _ioDispatcher),
				new PersistentSubscriptionMessageParker(key, _ioDispatcher)));

		var updateEntry = new PersistentSubscriptionEntry {
			Stream = stream,
			IndexName = indexName,
			Group = message.GroupName,
			ResolveLinkTos = message.ResolveLinkTos,
			CheckPointAfter = message.CheckPointAfterMilliseconds,
			ExtraStatistics = message.RecordStatistics,
			HistoryBufferSize = message.BufferSize,
			LiveBufferSize = message.LiveBufferSize,
			MaxCheckPointCount = message.MaxCheckPointCount,
			MinCheckPointCount = message.MinCheckPointCount,
			MaxRetryCount = message.MaxRetryCount,
			ReadBatchSize = message.ReadBatchSize,
			MaxSubscriberCount = message.MaxSubscriberCount,
			MessageTimeout = message.MessageTimeoutMilliseconds,
			NamedConsumerStrategy = message.NamedConsumerStrategy,
#pragma warning disable 612
			StartFrom = long.MinValue,
#pragma warning restore 612
			StartPosition = startFrom.ToString()
		};

		UpdateSubscription(stream, message.GroupName, subscription);
		UpdateSubscriptionConfig(message.User?.Identity?.Name, stream, message.GroupName, updateEntry);
		SaveConfiguration(() => message.Envelope.ReplyWith(
			new ClientMessage.UpdatePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.UpdatePersistentSubscriptionToIndexCompleted.UpdatePersistentSubscriptionToIndexResult.Success,
				"")));
	}

	public void Handle(ClientMessage.DeletePersistentSubscriptionToIndex message) {
		var indexName = message.IndexName;
		var eventSource = new PersistentSubscriptionIndexEventSource(indexName);
		var stream = eventSource.ToString();
		var key = BuildSubscriptionGroupKey(stream, message.GroupName);
		Log.Debug("Deleting persistent subscription to index {subscriptionKey}", key);

		if (!_subscriptionsById.TryGetValue(key, out var subscription)) {
			message.Envelope.ReplyWith(new ClientMessage.DeletePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.DeletePersistentSubscriptionToIndexCompleted.DeletePersistentSubscriptionToIndexResult.DoesNotExist,
				$"Group '{message.GroupName}' does not exist."));
			return;
		}

		UpdateSubscription(stream, message.GroupName, null);
		UpdateSubscriptionConfig(message.User?.Identity?.Name, stream, message.GroupName, null);
		subscription.Delete();
		SaveConfiguration(() => message.Envelope.ReplyWith(
			new ClientMessage.DeletePersistentSubscriptionToIndexCompleted(
				message.CorrelationId,
				ClientMessage.DeletePersistentSubscriptionToIndexCompleted.DeletePersistentSubscriptionToIndexResult.Success,
				"")));
	}

	ValueTask IAsyncHandle<ClientMessage.ConnectToPersistentSubscriptionToIndex>.HandleAsync(
		ClientMessage.ConnectToPersistentSubscriptionToIndex message, CancellationToken token) {
		var indexName = message.IndexName;
		var eventSource = new PersistentSubscriptionIndexEventSource(indexName);
		var stream = eventSource.ToString();

		if (!_subscriptionTopics.TryGetValue(stream, out _)) {
			message.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(
				message.CorrelationId, SubscriptionDropReason.NotFound));
			return ValueTask.CompletedTask;
		}

		var key = BuildSubscriptionGroupKey(stream, message.GroupName);
		if (!_subscriptionsById.TryGetValue(key, out var subscription)) {
			message.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(
				message.CorrelationId, SubscriptionDropReason.NotFound));
			return ValueTask.CompletedTask;
		}

		if (subscription.HasReachedMaxClientCount) {
			message.Envelope.ReplyWith(new ClientMessage.SubscriptionDropped(
				message.CorrelationId, SubscriptionDropReason.SubscriberMaxCountReached));
			return ValueTask.CompletedTask;
		}

		Log.Debug("New connection to persistent subscription to index {subscriptionKey} by {connectionId}",
			key, message.ConnectionId);

		var subscribedMessage = new ClientMessage.PersistentSubscriptionConfirmation(
			key, message.CorrelationId, 0, null);
		message.Envelope.ReplyWith(subscribedMessage);

		var name = message.User?.Identity?.Name ?? "anonymous";
		subscription.AddClient(message.CorrelationId, message.ConnectionId, message.ConnectionName,
			message.Envelope, message.AllowedInFlightMessages, name, message.From);

		return ValueTask.CompletedTask;
	}

	ValueTask IAsyncHandle<StorageMessage.SecondaryIndexCommitted>.HandleAsync(
		StorageMessage.SecondaryIndexCommitted message, CancellationToken token) {
		var stream = IndexNameToStream(message.IndexName);
		if (!_subscriptionTopics.TryGetValue(stream, out var subscriptions))
			return ValueTask.CompletedTask;

		for (int i = 0, n = subscriptions.Count; i < n; i++) {
			subscriptions[i].NotifyLiveSubscriptionMessage(message.Event);
		}

		return ValueTask.CompletedTask;
	}

	ValueTask IAsyncHandle<StorageMessage.SecondaryIndexDeleted>.HandleAsync(
		StorageMessage.SecondaryIndexDeleted message, CancellationToken token) {
		var subscriptionsToRemove = new List<(string stream, string group, PersistentSubscription sub)>();

		foreach (var (stream, subscriptions) in _subscriptionTopics) {
			// The stream key is in the format "$index-{indexName}"; the regex matches against the raw index name.
			if (!message.StreamIdRegex.IsMatch(stream))
				continue;

			foreach (var sub in subscriptions) {
				subscriptionsToRemove.Add((stream, sub.GroupName, sub));
			}
		}

		foreach (var (stream, group, sub) in subscriptionsToRemove) {
			Log.Warning(
				"Dropping persistent subscription to index '{stream}::{group}' because the index was deleted.",
				stream, group);
			sub.Delete();
			sub.Shutdown();
			UpdateSubscription(stream, group, null);
			UpdateSubscriptionConfig(updatedBy: null, stream, group, replaceBy: null);
		}

		if (subscriptionsToRemove.Count > 0) {
			SaveConfiguration(() => { });
		}

		return ValueTask.CompletedTask;
	}

	private bool TryCreateSubscriptionGroup(
		IPersistentSubscriptionEventSource eventSource,
		string groupName,
		bool resolveLinkTos,
		IPersistentSubscriptionStreamPosition startFrom,
		bool extraStatistics,
		int maxRetryCount,
		int liveBufferSize,
		int historyBufferSize,
		int readBatchSize,
		TimeSpan checkPointAfter,
		int minCheckPointCount,
		int maxCheckPointCount,
		int maxSubscriberCount,
		string namedConsumerStrategy,
		TimeSpan messageTimeout) {
		var stream = eventSource.ToString();
		var key = BuildSubscriptionGroupKey(stream, groupName);

		if (_subscriptionsById.ContainsKey(key))
			return false;

		var subscription = new PersistentSubscription(
			new PersistentSubscriptionParams(
				resolveLinkTos,
				key,
				eventSource,
				groupName,
				startFrom,
				extraStatistics,
				messageTimeout,
				maxRetryCount,
				liveBufferSize,
				historyBufferSize,
				readBatchSize,
				checkPointAfter,
				minCheckPointCount,
				maxCheckPointCount,
				maxSubscriberCount,
				_consumerStrategyRegistry.GetInstance(namedConsumerStrategy, key),
				_streamReader,
				_checkpointReader,
				new PersistentSubscriptionCheckpointWriter(key, _ioDispatcher),
				new PersistentSubscriptionMessageParker(key, _ioDispatcher)));

		UpdateSubscription(stream, groupName, subscription);
		return true;
	}

	private void UpdateSubscription(string eventSource, string groupName, PersistentSubscription replaceBy) {
		var key = BuildSubscriptionGroupKey(eventSource, groupName);

		if (!_subscriptionTopics.TryGetValue(eventSource, out var subscribers)) {
			subscribers = new List<PersistentSubscription>();
			_subscriptionTopics.Add(eventSource, subscribers);
		}

		// shut down any existing subscription
		var subscriptionIndex = -1;
		for (int i = 0; i < subscribers.Count; i++) {
			if (subscribers[i].SubscriptionId != key)
				continue;

			subscriptionIndex = i;
			var sub = subscribers[i];
			try {
				sub.Shutdown();
			} catch (Exception ex) {
				Log.Error(ex, "Failed to shut down subscription with id: {subscriptionId}",
					sub.SubscriptionId);
			}
			break;
		}

		if (_subscriptionsById.ContainsKey(key)) {
			if (subscriptionIndex == -1)
				throw new ArgumentException($"Subscription: '{key}' exists but it's not present in the list of subscribers");

			if (replaceBy != null) {
				_subscriptionsById[key] = replaceBy;
				subscribers[subscriptionIndex] = replaceBy;
			} else {
				_subscriptionsById.Remove(key);
				subscribers.RemoveAt(subscriptionIndex);
				if (subscribers.Count == 0)
					_subscriptionTopics.Remove(eventSource);
			}
		} else {
			if (subscriptionIndex != -1)
				throw new ArgumentException($"Subscription: '{key}' does not exist but it's present in the list of subscribers");

			// create
			_subscriptionsById.Add(key, replaceBy);
			subscribers.Add(replaceBy);
		}
	}

	private void UpdateSubscriptionConfig(string updatedBy, string eventSource, string groupName,
		PersistentSubscriptionEntry replaceBy) {
		_config.Updated = DateTime.Now;
		_config.UpdatedBy = updatedBy;
		var index = _config.Entries.FindLastIndex(x =>
			x.IndexName != null && x.Stream == eventSource && x.Group == groupName);

		if (index < 0) {
			if (replaceBy == null) {
				var key = BuildSubscriptionGroupKey(eventSource, groupName);
				throw new ArgumentException($"Config for subscription: '{key}' does not exist");
			}
			// create
			_config.Entries.Add(replaceBy);
		} else {
			if (replaceBy != null) // update
				_config.Entries[index] = replaceBy;
			else // delete
				_config.Entries.RemoveAt(index);
		}
	}

	private void LoadConfiguration(Action continueWith) {
		_ioDispatcher.ReadBackward(SystemStreams.PersistentSubscriptionConfig, -1, 1, false,
			SystemAccounts.System,
			x => HandleLoadCompleted(continueWith, x),
			expires: ClientMessage.ReadRequestMessage.NeverExpires);
	}

	private void HandleLoadCompleted(Action continueWith,
		ClientMessage.ReadStreamEventsBackwardCompleted completed) {
		switch (completed.Result) {
			case ReadStreamResult.Success:
				try {
					_config = PersistentSubscriptionConfig.FromSerializedForm(completed.Events[0].Event.Data);
					// Only keep index entries in our config; stream/all entries belong to the main service.
					_config.Entries = _config.Entries.Where(e => e.IndexName != null).ToList();
				} catch (Exception ex) {
					Log.Error(ex, "There was an error loading index subscription configuration from storage.");
				}
				continueWith();
				break;
			case ReadStreamResult.NoStream:
				_config = new PersistentSubscriptionConfig { Version = "2" };
				continueWith();
				break;
			default:
				throw new Exception(completed.Result +
									" is an unexpected result reading subscription configuration.");
		}
	}

	private void SaveConfiguration(Action continueWith) {
		// Re-read the full config, merge our index entries, then write it back.
		// This avoids clobbering entries that belong to the main service.
		_ioDispatcher.ReadBackward(SystemStreams.PersistentSubscriptionConfig, -1, 1, false,
			SystemAccounts.System,
			x => HandleSaveReadCompleted(continueWith, x),
			expires: ClientMessage.ReadRequestMessage.NeverExpires);
	}

	private void HandleSaveReadCompleted(Action continueWith,
		ClientMessage.ReadStreamEventsBackwardCompleted completed) {
		PersistentSubscriptionConfig fullConfig;
		switch (completed.Result) {
			case ReadStreamResult.Success:
				try {
					fullConfig = PersistentSubscriptionConfig.FromSerializedForm(completed.Events[0].Event.Data);
				} catch (Exception ex) {
					Log.Error(ex, "Error reading config for merge during save.");
					return;
				}
				break;
			case ReadStreamResult.NoStream:
				fullConfig = new PersistentSubscriptionConfig { Version = "2" };
				break;
			default:
				Log.Error("Unexpected result {result} reading config for merge during save.", completed.Result);
				return;
		}

		// Remove all index entries from full config and replace with ours
		fullConfig.Entries.RemoveAll(e => e.IndexName != null);
		fullConfig.Entries.AddRange(_config.Entries);
		fullConfig.Updated = _config.Updated;
		fullConfig.UpdatedBy = _config.UpdatedBy;

		WriteMergedConfig(fullConfig, continueWith);
	}

	private void WriteMergedConfig(PersistentSubscriptionConfig config, Action continueWith) {
		Log.Debug("Saving persistent subscription index configuration");
		var data = config.GetSerializedForm();
		var ev = new Event(Guid.NewGuid(), SystemEventTypes.PersistentSubscriptionConfig, true, data);
		var metadata = new StreamMetadata(maxCount: 2);
		var streamMetadata = new Lazy<StreamMetadata>(() => metadata);
		var events = new[] { ev };
		_ioDispatcher.ConfigureStreamAndWriteEvents(SystemStreams.PersistentSubscriptionConfig,
			ExpectedVersion.Any, streamMetadata, events, SystemAccounts.System,
			x => HandleWriteCompleted(continueWith, x));
	}

	private void HandleWriteCompleted(Action continueWith, ClientMessage.WriteEventsCompleted obj) {
		switch (obj.Result) {
			case OperationResult.Success:
				continueWith();
				break;
			case OperationResult.CommitTimeout:
			case OperationResult.PrepareTimeout:
				Log.Information("Timeout while trying to save persistent subscription index configuration. Retrying.");
				SaveConfiguration(continueWith);
				break;
			default:
				throw new Exception(obj.Result +
									" is an unexpected result writing persistent subscription index configuration.");
		}
	}

	private static string IndexNameToStream(string indexName) {
		return $"$index-{indexName}";
	}

	private static string BuildSubscriptionGroupKey(string stream, string groupName) {
		return stream + "::" + groupName;
	}

	private static TimeSpan ToCheckPointAfterTimeout(int milliseconds) {
		return milliseconds == 0 ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds);
	}

	private static TimeSpan ToMessageTimeout(int milliseconds) {
		return milliseconds == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds);
	}
}
