// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.SingleStream;

public class StreamPositionTagger : PositionTagger {
	private readonly string _stream;

	public StreamPositionTagger(int phase, string stream) : base(phase) {
		if (stream == null) throw new ArgumentNullException("stream");
		if (string.IsNullOrEmpty(stream)) throw new ArgumentException("stream");
		_stream = stream;
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		if (previous.Mode_ != CheckpointTag.Mode.Stream)
			throw new ArgumentException("Mode.Stream expected", "previous");
		return committedEvent.Data.PositionStreamId == _stream
		       && committedEvent.Data.PositionSequenceNumber > previous.Streams[_stream];
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected: {0} Was: {1}", Phase, previous.Phase));

		if (committedEvent.Data.PositionStreamId != _stream)
			throw new InvalidOperationException(
				string.Format(
					"Invalid stream '{0}'.  Expected stream is '{1}'", committedEvent.Data.EventStreamId, _stream));
		return CheckpointTag.FromStreamPosition(previous.Phase, committedEvent.Data.PositionStreamId,
			committedEvent.Data.PositionSequenceNumber);
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionEof partitionEof) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		if (previous.Phase != Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected: {0} Was: {1}", Phase, previous.Phase));

		if (partitionDeleted.PositionStreamId != _stream)
			throw new InvalidOperationException(
				string.Format(
					"Invalid stream '{0}'.  Expected stream is '{1}'", partitionDeleted.Partition, _stream));

		// return ordinary checkpoint tag (suitable for fromCategory.foreachStream as well as for regular fromStream
		return CheckpointTag.FromStreamPosition(
			previous.Phase, partitionDeleted.PositionStreamId, partitionDeleted.PositionEventNumber.Value);
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromStreamPosition(Phase, _stream, -1);
	}

	public override bool IsCompatible(CheckpointTag checkpointTag) {
		return checkpointTag.Mode_ == CheckpointTag.Mode.Stream && checkpointTag.Streams.Keys.First() == _stream;
	}

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected less or equal to: {0} Was: {1}", Phase,
					tag.Phase), "tag");


		if (tag.Mode_ == CheckpointTag.Mode.Stream) {
			long p;
			return CheckpointTag.FromStreamPosition(
				tag.Phase, _stream, tag.Streams.TryGetValue(_stream, out p) ? p : -1);
		}

		switch (tag.Mode_) {
			case CheckpointTag.Mode.EventTypeIndex:
				throw new NotSupportedException(
					"Conversion from EventTypeIndex to Stream position tag is not supported");
			case CheckpointTag.Mode.PreparePosition:
				throw new NotSupportedException(
					"Conversion from PreparePosition to Stream position tag is not supported");
			case CheckpointTag.Mode.MultiStream:
				long p;
				return CheckpointTag.FromStreamPosition(
					tag.Phase, _stream, tag.Streams.TryGetValue(_stream, out p) ? p : -1);
			case CheckpointTag.Mode.Position:
				throw new NotSupportedException("Conversion from Position to Stream position tag is not supported");
			default:
				throw new NotSupportedException(string.Format(
					"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {0}",
					tag.ToString()));
		}
	}
}
