// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.AllStream;

public class TransactionFilePositionTagger : PositionTagger {
	public TransactionFilePositionTagger(int phase)
		: base(phase) {
	}

	public override bool IsCompatible(CheckpointTag checkpointTag) {
		return checkpointTag.Mode_ == CheckpointTag.Mode.Position;
	}

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected less or equal to: {0} Was: {1}", Phase,
					tag.Phase), "tag");

		if (tag.Mode_ == CheckpointTag.Mode.Position)
			return tag;

		switch (tag.Mode_) {
			case CheckpointTag.Mode.EventTypeIndex:
				return CheckpointTag.FromPosition(
					tag.Phase, tag.Position.CommitPosition, tag.Position.PreparePosition);
			case CheckpointTag.Mode.Stream:
				throw new NotSupportedException("Conversion from Stream to Position position tag is not supported");
			case CheckpointTag.Mode.MultiStream:
				throw new NotSupportedException(
					"Conversion from MultiStream to Position position tag is not supported");
			case CheckpointTag.Mode.PreparePosition:
				throw new NotSupportedException(
					"Conversion from PreparePosition to Position position tag is not supported");
			default:
				throw new NotSupportedException(string.Format(
					"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {0}",
					tag.ToString()));
		}
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		if (previous.Mode_ != CheckpointTag.Mode.Position)
			throw new ArgumentException("Mode.Position expected", "previous");
		return committedEvent.Data.Position > previous.Position;
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected: {0} Was: {1}", Phase, previous.Phase));

		return CheckpointTag.FromPosition(previous.Phase, committedEvent.Data.Position);
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionEof partitionEof) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		if (previous.Phase != Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected: {0} Was: {1}", Phase, previous.Phase));

		if (partitionDeleted.DeleteLinkOrEventPosition == null)
			throw new ArgumentException(
				"Invalid partiton deleted message. deleteEventOrLinkTargetPosition required");

		return CheckpointTag.FromPosition(previous.Phase, partitionDeleted.DeleteLinkOrEventPosition.Value);
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromPosition(Phase, 0, -1);
	}
}
