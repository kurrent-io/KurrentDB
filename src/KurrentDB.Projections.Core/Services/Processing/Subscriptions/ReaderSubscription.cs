// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;

namespace KurrentDB.Projections.Core.Services.Processing.Subscriptions;

public class ReaderSubscription(
	IPublisher publisher,
	Guid subscriptionId,
	CheckpointTag from,
	IReaderStrategy readerStrategy,
	ITimeProvider timeProvider,
	long? checkpointUnhandledBytesThreshold,
	int? checkpointProcessedEventsThreshold,
	int checkpointAfterMs,
	bool stopOnEof,
	int? stopAfterNEvents,
	bool enableContentTypeValidation)
	: ReaderSubscriptionBase(publisher,
			subscriptionId,
			from,
			readerStrategy,
			timeProvider,
			checkpointUnhandledBytesThreshold,
			checkpointProcessedEventsThreshold,
			checkpointAfterMs,
			stopOnEof,
			stopAfterNEvents,
			enableContentTypeValidation),
		IReaderSubscription {
	public void Handle(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		ProcessOne(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderIdle message) {
		ForceProgressValue(100);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		if (!EventFilter.PassesDeleteNotification(message.PositionStreamId))
			return;
		var deletePosition = PositionTagger.MakeCheckpointTag(PositionTracker.LastTag, message);
		PublishPartitionDeleted(message.Partition, deletePosition);
	}

	public void Handle(ReaderSubscriptionMessage.ReportProgress message) {
		NotifyProgress();
	}
}
