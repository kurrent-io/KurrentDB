// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public abstract class PositionTagger {
	public readonly int Phase;

	public PositionTagger(int phase) {
		Phase = phase;
	}

	public abstract bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent);

	public abstract CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent);

	public abstract CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted);

	public abstract CheckpointTag MakeZeroCheckpointTag();

	public abstract bool IsCompatible(CheckpointTag checkpointTag);

	public abstract CheckpointTag AdjustTag(CheckpointTag tag);
}
