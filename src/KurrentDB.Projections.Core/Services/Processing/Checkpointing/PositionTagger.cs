// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using static KurrentDB.Projections.Core.Messages.ReaderSubscriptionMessage;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public abstract class PositionTagger(int phase) {
	protected readonly int Phase = phase;

	public abstract bool IsMessageAfterCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent);

	public abstract CheckpointTag MakeCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent);

	public abstract CheckpointTag MakeCheckpointTag(CheckpointTag previous, EventReaderPartitionDeleted partitionDeleted);

	public abstract CheckpointTag MakeZeroCheckpointTag();

	public abstract bool IsCompatible(CheckpointTag checkpointTag);

	public abstract CheckpointTag AdjustTag(CheckpointTag tag);
}
