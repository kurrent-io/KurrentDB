// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using static KurrentDB.Projections.Core.Messages.ReaderSubscriptionMessage;

namespace KurrentDB.Projections.Core.Services.Processing.TransactionFile;

public class PreparePositionTagger(int phase) : PositionTagger(phase) {
	public override bool IsMessageAfterCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent) {
		return previous.Phase < Phase || committedEvent.Data.Position.PreparePosition > previous.PreparePosition;
	}

	public override CheckpointTag
		MakeCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent)
		=> previous.Phase != Phase
			? throw new ArgumentException($"Invalid checkpoint tag phase.  Expected: {Phase} Was: {previous.Phase}")
			: CheckpointTag.FromPreparePosition(previous.Phase, committedEvent.Data.Position.PreparePosition);

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, EventReaderPartitionDeleted partitionDeleted) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeZeroCheckpointTag() => CheckpointTag.FromPreparePosition(Phase, -1);

	public override bool IsCompatible(CheckpointTag checkpointTag) => checkpointTag.Mode_ == CheckpointTag.Mode.PreparePosition;

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase. Expected less or equal to: {Phase} Was: {tag.Phase}", nameof(tag));

		if (tag.Mode_ == CheckpointTag.Mode.PreparePosition)
			return tag;

		throw tag.Mode_ switch {
			CheckpointTag.Mode.EventTypeIndex => new("Conversion from EventTypeIndex to PreparePosition position tag is not supported"),
			CheckpointTag.Mode.Stream => new("Conversion from Stream to PreparePosition position tag is not supported"),
			CheckpointTag.Mode.MultiStream => new("Conversion from MultiStream to PreparePosition position tag is not supported"),
			CheckpointTag.Mode.Position => new("Conversion from Position to PreparePosition position tag is not supported"),
			_ => new NotSupportedException(
				$"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {tag}")
		};
	}
}
