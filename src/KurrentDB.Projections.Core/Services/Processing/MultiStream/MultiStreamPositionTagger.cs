// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.MultiStream;

public class MultiStreamPositionTagger : PositionTagger {
	private readonly HashSet<string> _streams;

	public MultiStreamPositionTagger(int phase, string[] streams) : base(phase) {
		ArgumentNullException.ThrowIfNull(streams);
		if (streams.Length == 0)
			throw new ArgumentException(null, nameof(streams));
		_streams = new(streams);
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		if (previous.Mode_ != CheckpointTag.Mode.MultiStream)
			throw new ArgumentException("Mode.MultiStream expected", nameof(previous));
		return _streams.Contains(committedEvent.Data.PositionStreamId)
			   && committedEvent.Data.PositionSequenceNumber >
			   previous.Streams[committedEvent.Data.PositionStreamId];
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase.  Expected: {Phase} Was: {previous.Phase}");

		if (!_streams.Contains(committedEvent.Data.PositionStreamId))
			throw new InvalidOperationException($"Invalid stream '{committedEvent.Data.EventStreamId}'");
		return previous.UpdateStreamPosition(committedEvent.Data.PositionStreamId, committedEvent.Data.PositionSequenceNumber);
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromStreamPositions(Phase,
			_streams.ToDictionary(v => v, _ => ExpectedVersion.NoStream));
	}

	public override bool IsCompatible(CheckpointTag checkpointTag) {
		//TODO: should Stream be supported here as well if in the set?
		return checkpointTag.Mode_ == CheckpointTag.Mode.MultiStream
			   && checkpointTag.Streams.All(v => _streams.Contains(v.Key));
	}

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException(
				$"Invalid checkpoint tag phase.  Expected less or equal to: {Phase} Was: {tag.Phase}", nameof(tag));

		if (tag.Mode_ == CheckpointTag.Mode.MultiStream) {
			return CheckpointTag.FromStreamPositions(
				tag.Phase, _streams.ToDictionary(v => v, v => tag.Streams.TryGetValue(v, out var p) ? p : -1));
		}

		return tag.Mode_ switch {
			CheckpointTag.Mode.EventTypeIndex => throw new NotSupportedException(
				"Conversion from EventTypeIndex to MultiStream position tag is not supported"),
			CheckpointTag.Mode.Stream => CheckpointTag.FromStreamPositions(tag.Phase,
				_streams.ToDictionary(v => v, v => tag.Streams.TryGetValue(v, out var p) ? p : -1)),
			CheckpointTag.Mode.PreparePosition => throw new NotSupportedException(
				"Conversion from PreparePosition to MultiStream position tag is not supported"),
			CheckpointTag.Mode.Position => throw new NotSupportedException(
				"Conversion from Position to MultiStream position tag is not supported"),
			_ => throw new NotSupportedException(
				$"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {tag}")
		};
	}
}
