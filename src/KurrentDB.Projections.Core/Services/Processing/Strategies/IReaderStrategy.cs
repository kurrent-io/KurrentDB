// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

public interface IReaderStrategy {
	bool IsReadingOrderRepeatable { get; }
	EventFilter EventFilter { get; }
	PositionTagger PositionTagger { get; }

	IReaderSubscription CreateReaderSubscription(
		IPublisher publisher, CheckpointTag fromCheckpointTag, Guid subscriptionId,
		ReaderSubscriptionOptions readerSubscriptionOptions);

	IEventReader CreatePausedEventReader(Guid eventReaderId, IPublisher publisher, CheckpointTag checkpointTag, bool stopOnEof);
}
