// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.AllStream;

public class PreparePositionTagger : PositionTagger {
	public PreparePositionTagger(int phase)
		: base(phase) {
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		return committedEvent.Data.Position.PreparePosition > previous.PreparePosition;
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected: {0} Was: {1}", Phase, previous.Phase));

		return CheckpointTag.FromPreparePosition(previous.Phase, committedEvent.Data.Position.PreparePosition);
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionEof partitionEof) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromPreparePosition(Phase, -1);
	}

	public override bool IsCompatible(CheckpointTag checkpointTag) {
		return checkpointTag.Mode_ == CheckpointTag.Mode.PreparePosition;
	}

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException(
				string.Format(
					"Invalid checkpoint tag phase.  Expected less or equal to: {0} Was: {1}", Phase, tag.Phase),
				"tag");

		if (tag.Mode_ == CheckpointTag.Mode.PreparePosition)
			return tag;

		switch (tag.Mode_) {
			case CheckpointTag.Mode.EventTypeIndex:
				throw new NotSupportedException(
					"Conversion from EventTypeIndex to PreparePosition position tag is not supported");
			case CheckpointTag.Mode.Stream:
				throw new NotSupportedException(
					"Conversion from Stream to PreparePosition position tag is not supported");
			case CheckpointTag.Mode.MultiStream:
				throw new NotSupportedException(
					"Conversion from MultiStream to PreparePosition position tag is not supported");
			case CheckpointTag.Mode.Position:
				throw new NotSupportedException(
					"Conversion from Position to PreparePosition position tag is not supported");
			default:
				throw new NotSupportedException(string.Format(
					"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {0}",
					tag.ToString()));
		}
	}
}
