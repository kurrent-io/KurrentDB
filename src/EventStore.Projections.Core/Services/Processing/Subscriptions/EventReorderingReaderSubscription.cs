// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;

namespace EventStore.Projections.Core.Services.Processing.Subscriptions;

public class EventReorderingReaderSubscription : ReaderSubscriptionBase, IReaderSubscription {
	private readonly SortedList<long, ReaderSubscriptionMessage.CommittedEventDistributed> _buffer =
		new SortedList<long, ReaderSubscriptionMessage.CommittedEventDistributed>();

	private readonly int _processingLagMs;

	public EventReorderingReaderSubscription(
		IPublisher publisher,
		Guid subscriptionId,
		CheckpointTag @from,
		IReaderStrategy readerStrategy,
		ITimeProvider timeProvider,
		long? checkpointUnhandledBytesThreshold,
		int? checkpointProcessedEventsThreshold,
		int checkpointAfterMs,
		int processingLagMs,
		bool stopOnEof,
		int? stopAfterNEvents,
		bool enableContentTypeValidation)
		: base(
			publisher,
			subscriptionId,
			@from,
			readerStrategy,
			timeProvider,
			checkpointUnhandledBytesThreshold,
			checkpointProcessedEventsThreshold,
			checkpointAfterMs,
			stopOnEof,
			stopAfterNEvents,
			enableContentTypeValidation) {
		_processingLagMs = processingLagMs;
	}

	public void Handle(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		if (message.Data == null)
			throw new NotSupportedException();
		ReaderSubscriptionMessage.CommittedEventDistributed existing;
		// ignore duplicate messages (when replaying from heading event distribution point)
		if (!_buffer.TryGetValue(message.Data.Position.PreparePosition, out existing)) {
			_buffer.Add(message.Data.Position.PreparePosition, message);
			var maxTimestamp = _buffer.Max(v => v.Value.Data.Timestamp);
			ProcessAllFor(maxTimestamp);
		}
	}

	private void ProcessAllFor(DateTime maxTimestamp) {
		//NOTE: this is the most straightforward implementation
		//TODO: build proper data structure when the approach is finalized
		bool processed;
		do {
			processed = ProcessFor(maxTimestamp);
		} while (processed);
	}

	private bool ProcessFor(DateTime maxTimestamp) {
		if (_buffer.Count == 0)
			return false;
		var first = _buffer.ElementAt(0);
		if ((maxTimestamp - first.Value.Data.Timestamp).TotalMilliseconds > _processingLagMs) {
			_buffer.RemoveAt(0);
			ProcessOne(first.Value);
			return true;
		}

		return false;
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderIdle message) {
		ProcessAllFor(message.IdleTimestampUtc);
	}


	protected override void EofReached() {
		// flush all available events as wqe reached eof (currently onetime projections only)
		ProcessAllFor(DateTime.MaxValue);
	}

	public new void Handle(ReaderSubscriptionMessage.EventReaderPartitionEof message) {
		throw new NotSupportedException();
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		throw new NotSupportedException();
	}

	public void Handle(ReaderSubscriptionMessage.ReportProgress message) {
		NotifyProgress();
	}
}
