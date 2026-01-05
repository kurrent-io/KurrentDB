// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Services.AwakeReaderService;

// The AwakerService notifies subscribers when new events are committed to particular streams or $all
// It calls the callbacks when it reaches the end of the log (or a threshold) rather than immediately on new event because
// the subscriber will usually issue a read, so may as well wait until the events currently being written are available to
// be returned in that read, saving multiple resubscriptions.
public class AwakeService :
	IHandle<AwakeServiceMessage.SubscribeAwake>,
	IHandle<AwakeServiceMessage.UnsubscribeAwake>,
	IHandle<StorageMessage.EventCommitted>,
	IHandle<StorageMessage.IndexedToEndOfTransactionFile> {
	private readonly Dictionary<string, HashSet<AwakeServiceMessage.SubscribeAwake>> _subscribers =
		new Dictionary<string, HashSet<AwakeServiceMessage.SubscribeAwake>>();

	private readonly Dictionary<Guid, AwakeServiceMessage.SubscribeAwake> _map =
		new Dictionary<Guid, AwakeServiceMessage.SubscribeAwake>();

	private TFPos _lastPosition;

	private readonly List<AwakeServiceMessage.SubscribeAwake> _batchedReplies =
		new List<AwakeServiceMessage.SubscribeAwake>();

	private int _processedEvents;
	private int _processedEventsAwakeThreshold = 1000;

	private void BeginReplyBatch() {
		if (_batchedReplies.Count > 0)
			throw new Exception();
		_processedEvents = 0;
	}

	private void EndReplyBatch() {
		foreach (var subscriber in _batchedReplies) {
			subscriber.Envelope.ReplyWith(subscriber.ReplyWithMessage);
		}

		_batchedReplies.Clear();
	}

	private void CheckProcessedEventThreshold() {
		if (_processedEvents > _processedEventsAwakeThreshold) {
			EndReplyBatch();
			BeginReplyBatch();
		}
	}

	public void Handle(AwakeServiceMessage.SubscribeAwake message) {
		//TODO: consider buffering last 10 events to avoid race condition
		// when someone writes before we subscribe forcing us to resubscribe
		if (message.From < _lastPosition) {
			message.Envelope.ReplyWith(message.ReplyWithMessage);
			return;
		}

		_map.Add(message.CorrelationId, message);
		HashSet<AwakeServiceMessage.SubscribeAwake> list;
		string streamId = message.StreamId ?? "$all";
		if (!_subscribers.TryGetValue(streamId, out list)) {
			list = new HashSet<AwakeServiceMessage.SubscribeAwake>();
			_subscribers.Add(streamId, list);
		}

		list.Add(message);
	}

	public void Handle(StorageMessage.EventCommitted message) {
		_processedEvents++;
		_lastPosition = new TFPos(message.CommitPosition, message.Event.LogPosition);

		if (EventFilter.DefaultAllFilter.IsEventAllowed(message.Event)) {
			NotifyEventInStream("$all", message);
		}

		if (EventFilter.DefaultStreamFilter.IsEventAllowed(message.Event)) {
			NotifyEventInStream(message.Event.EventStreamId, message);
		}

		if (message.TfEof) {
			EndReplyBatch();
			BeginReplyBatch();
		}

		CheckProcessedEventThreshold();
	}

	private void NotifyEventInStream(string streamId, StorageMessage.EventCommitted message) {
		HashSet<AwakeServiceMessage.SubscribeAwake> list;
		List<AwakeServiceMessage.SubscribeAwake> toRemove = null;
		if (_subscribers.TryGetValue(streamId, out list)) {
			foreach (var subscriber in list) {
				if (subscriber.From < new TFPos(message.CommitPosition, message.Event.LogPosition)) {
					_batchedReplies.Add(subscriber);
					_map.Remove(subscriber.CorrelationId);
					if (toRemove == null)
						toRemove = new List<AwakeServiceMessage.SubscribeAwake>();
					toRemove.Add(subscriber);
				}
			}

			if (toRemove != null) {
				foreach (var item in toRemove)
					list.Remove(item);
				if (list.Count == 0) {
					_subscribers.Remove(streamId);
				}
			}
		}
	}

	public void Handle(AwakeServiceMessage.UnsubscribeAwake message) {
		AwakeServiceMessage.SubscribeAwake subscriber;
		if (_map.TryGetValue(message.CorrelationId, out subscriber)) {
			_map.Remove(message.CorrelationId);
			var list = _subscribers[subscriber.StreamId ?? "$all"];
			list.Remove(subscriber);
		}
	}

	public void Handle(StorageMessage.IndexedToEndOfTransactionFile message) {
		EndReplyBatch();
		BeginReplyBatch();
	}
}
