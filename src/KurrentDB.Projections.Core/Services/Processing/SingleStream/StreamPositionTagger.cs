// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using static KurrentDB.Projections.Core.Messages.ReaderSubscriptionMessage;

namespace KurrentDB.Projections.Core.Services.Processing.SingleStream;

public class StreamPositionTagger : PositionTagger {
	private readonly string _stream;

	public StreamPositionTagger(int phase, string stream) : base(phase) {
		ArgumentException.ThrowIfNullOrEmpty(stream);
		_stream = stream;
	}

	public override bool IsMessageAfterCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		if (previous.Mode_ != CheckpointTag.Mode.Stream)
			throw new ArgumentException("Mode.Stream expected", nameof(previous));
		return committedEvent.Data.PositionStreamId == _stream
		       && committedEvent.Data.PositionSequenceNumber > previous.Streams[_stream];
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase.  Expected: {Phase} Was: {previous.Phase}");

		if (committedEvent.Data.PositionStreamId != _stream)
			throw new InvalidOperationException($"Invalid stream '{committedEvent.Data.EventStreamId}'.  Expected stream is '{_stream}'");
		return CheckpointTag.FromStreamPosition(previous.Phase, committedEvent.Data.PositionStreamId,
			committedEvent.Data.PositionSequenceNumber);
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, EventReaderPartitionDeleted partitionDeleted) {
		if (previous.Phase != Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase.  Expected: {Phase} Was: {previous.Phase}");

		if (partitionDeleted.PositionStreamId != _stream)
			throw new InvalidOperationException($"Invalid stream '{partitionDeleted.Partition}'.  Expected stream is '{_stream}'");

		// return ordinary checkpoint tag (suitable for fromCategory.foreachStream as well as for regular fromStream
		return CheckpointTag.FromStreamPosition(previous.Phase, partitionDeleted.PositionStreamId,
			partitionDeleted.PositionEventNumber.Value);
	}

	public override CheckpointTag MakeZeroCheckpointTag() => CheckpointTag.FromStreamPosition(Phase, _stream, -1);

	public override bool IsCompatible(CheckpointTag checkpointTag)
		=> checkpointTag.Mode_ == CheckpointTag.Mode.Stream && checkpointTag.Streams.Keys.First() == _stream;

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase.  Expected less or equal to: {Phase} Was: {tag.Phase}", nameof(tag));

		if (tag.Mode_ == CheckpointTag.Mode.Stream) {
			return CheckpointTag.FromStreamPosition(tag.Phase, _stream, tag.Streams.TryGetValue(_stream, out var p) ? p : -1);
		}

		return tag.Mode_ switch {
			CheckpointTag.Mode.EventTypeIndex => throw new NotSupportedException("Conversion from EventTypeIndex to Stream position tag is not supported"),
			CheckpointTag.Mode.PreparePosition => throw new NotSupportedException("Conversion from PreparePosition to Stream position tag is not supported"),
			CheckpointTag.Mode.MultiStream => CheckpointTag.FromStreamPosition(tag.Phase, _stream, tag.Streams.TryGetValue(_stream, out var p) ? p : -1),
			CheckpointTag.Mode.Position => throw new NotSupportedException("Conversion from Position to Stream position tag is not supported"),
			_ => throw new NotSupportedException(
				$"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {tag}")
		};
	}
}
