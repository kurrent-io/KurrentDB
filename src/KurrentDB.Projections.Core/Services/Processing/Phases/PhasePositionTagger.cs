// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using static KurrentDB.Projections.Core.Messages.ReaderSubscriptionMessage;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public class PhasePositionTagger(int phase) : PositionTagger(phase) {
	public override bool IsMessageAfterCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, EventReaderPartitionDeleted partitionDeleted) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeZeroCheckpointTag() => CheckpointTag.FromPhase(Phase, completed: false);

	public override bool IsCompatible(CheckpointTag checkpointTag) => checkpointTag.Mode_ == CheckpointTag.Mode.Phase;

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException(
				$"Invalid checkpoint tag phase.  Expected less or equal to: {Phase} Was: {tag.Phase}", nameof(tag));

		return tag.Mode_ == CheckpointTag.Mode.Phase
			? tag
			: throw new NotSupportedException("Conversion to phase based checkpoint tag is not supported");
	}
}
