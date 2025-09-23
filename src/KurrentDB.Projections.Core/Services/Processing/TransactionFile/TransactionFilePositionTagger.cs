// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.TransactionFile;

public class TransactionFilePositionTagger(int phase) : PositionTagger(phase) {
	public override bool IsCompatible(CheckpointTag checkpointTag) => checkpointTag.Mode_ == CheckpointTag.Mode.Position;

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase. Expected less or equal to: {Phase} Was: {tag.Phase}", nameof(tag));

		if (tag.Mode_ == CheckpointTag.Mode.Position)
			return tag;

		return tag.Mode_ switch {
			CheckpointTag.Mode.EventTypeIndex => CheckpointTag.FromPosition(tag.Phase, tag.Position.CommitPosition, tag.Position.PreparePosition),
			CheckpointTag.Mode.Stream => throw new NotSupportedException("Conversion from Stream to Position position tag is not supported"),
			CheckpointTag.Mode.MultiStream => throw new NotSupportedException("Conversion from MultiStream to Position position tag is not supported"),
			CheckpointTag.Mode.PreparePosition => throw new NotSupportedException("Conversion from PreparePosition to Position position tag is not supported"),
			_ => throw new NotSupportedException(
				$"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {tag}")
		};
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous,
		ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		return previous.Phase < Phase
		       || (previous.Mode_ == CheckpointTag.Mode.Position
			       ? committedEvent.Data.Position > previous.Position
			       : throw new ArgumentException("Mode.Position expected", nameof(previous)));
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous,
		ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		return previous.Phase == Phase
			? CheckpointTag.FromPosition(previous.Phase, committedEvent.Data.Position)
			: throw new ArgumentException($"Invalid checkpoint tag phase. Expected: {Phase} Was: {previous.Phase}");
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		if (previous.Phase != Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase. Expected: {Phase} Was: {previous.Phase}");

		return partitionDeleted.DeleteLinkOrEventPosition != null
			? CheckpointTag.FromPosition(previous.Phase, partitionDeleted.DeleteLinkOrEventPosition.Value)
			: throw new ArgumentException("Invalid partition deleted message. deleteEventOrLinkTargetPosition required");
	}

	public override CheckpointTag MakeZeroCheckpointTag() => CheckpointTag.FromPosition(Phase, 0, -1);
}
