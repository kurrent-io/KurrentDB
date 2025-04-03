// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.Strategies;

public interface IReaderStrategy {
	bool IsReadingOrderRepeatable { get; }
	EventFilter EventFilter { get; }
	PositionTagger PositionTagger { get; }

	IReaderSubscription CreateReaderSubscription(
		IPublisher publisher, CheckpointTag fromCheckpointTag, Guid subscriptionId,
		ReaderSubscriptionOptions readerSubscriptionOptions);

	IEventReader CreatePausedEventReader(
		Guid eventReaderId, IPublisher publisher, IODispatcher ioDispatcher, CheckpointTag checkpointTag,
		bool stopOnEof, int? stopAfterNEvents);
}
