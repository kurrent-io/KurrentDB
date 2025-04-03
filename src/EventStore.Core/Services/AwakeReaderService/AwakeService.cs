// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Storage.ReaderIndex;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator

namespace EventStore.Core.Services.AwakeReaderService;

public class AwakeService : IHandle<AwakeServiceMessage.SubscribeAwake>,
	IHandle<AwakeServiceMessage.UnsubscribeAwake>,
	IHandle<StorageMessage.EventCommitted>,
	IHandle<StorageMessage.TfEofAtNonCommitRecord> {
	private readonly Dictionary<string, HashSet<AwakeServiceMessage.SubscribeAwake>> _subscribers = new();
	private readonly Dictionary<Guid, AwakeServiceMessage.SubscribeAwake> _map = new();

	private TFPos _lastPosition;

	private readonly List<AwakeServiceMessage.SubscribeAwake> _batchedReplies = [];

	private int _processedEvents;
	const int ProcessedEventsAwakeThreshold = 1000;

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
		if (_processedEvents > ProcessedEventsAwakeThreshold) {
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
		string streamId = message.StreamId ?? "$all";
		if (!_subscribers.TryGetValue(streamId, out var list)) {
			list = [];
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
		List<AwakeServiceMessage.SubscribeAwake> toRemove = null;
		if (!_subscribers.TryGetValue(streamId, out var list)) return;

		foreach (var subscriber in list) {
			if (subscriber.From >= new TFPos(message.CommitPosition, message.Event.LogPosition)) continue;

			_batchedReplies.Add(subscriber);
			_map.Remove(subscriber.CorrelationId);
			toRemove ??= [];
			toRemove.Add(subscriber);
		}

		if (toRemove == null) return;

		foreach (var item in toRemove)
			list.Remove(item);
		if (list.Count == 0) {
			_subscribers.Remove(streamId);
		}
	}

	public void Handle(AwakeServiceMessage.UnsubscribeAwake message) {
		if (!_map.Remove(message.CorrelationId, out var subscriber)) return;

		var list = _subscribers[subscriber.StreamId ?? "$all"];
		list.Remove(subscriber);
	}

	public void Handle(StorageMessage.TfEofAtNonCommitRecord message) {
		EndReplyBatch();
		BeginReplyBatch();
	}
}
