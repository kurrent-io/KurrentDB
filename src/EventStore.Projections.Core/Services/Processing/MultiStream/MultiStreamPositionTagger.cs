// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.MultiStream;

public class MultiStreamPositionTagger : PositionTagger {
	private readonly HashSet<string> _streams;

	public MultiStreamPositionTagger(int phase, string[] streams) : base(phase) {
		if (streams == null) throw new ArgumentNullException("streams");
		if (streams.Length == 0) throw new ArgumentException("streams");
		_streams = new HashSet<string>(streams);
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		if (previous.Mode_ != CheckpointTag.Mode.MultiStream)
			throw new ArgumentException("Mode.MultiStream expected", "previous");
		return _streams.Contains(committedEvent.Data.PositionStreamId)
		       && committedEvent.Data.PositionSequenceNumber >
		       previous.Streams[committedEvent.Data.PositionStreamId];
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException(
				string.Format("Invalid checkpoint tag phase.  Expected: {0} Was: {1}", Phase, previous.Phase));

		if (!_streams.Contains(committedEvent.Data.PositionStreamId))
			throw new InvalidOperationException(
				string.Format("Invalid stream '{0}'", committedEvent.Data.EventStreamId));
		return previous.UpdateStreamPosition(
			committedEvent.Data.PositionStreamId, committedEvent.Data.PositionSequenceNumber);
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionEof partitionEof) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
		ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		throw new NotSupportedException();
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromStreamPositions(Phase,
			_streams.ToDictionary(v => v, v => (long)ExpectedVersion.NoStream));
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
				string.Format("Invalid checkpoint tag phase.  Expected less or equal to: {0} Was: {1}", Phase,
					tag.Phase), "tag");

		if (tag.Mode_ == CheckpointTag.Mode.MultiStream) {
			long p;
			return CheckpointTag.FromStreamPositions(
				tag.Phase, _streams.ToDictionary(v => v, v => tag.Streams.TryGetValue(v, out p) ? p : -1));
		}

		switch (tag.Mode_) {
			case CheckpointTag.Mode.EventTypeIndex:
				throw new NotSupportedException(
					"Conversion from EventTypeIndex to MultiStream position tag is not supported");
			case CheckpointTag.Mode.Stream:
				long p;
				return CheckpointTag.FromStreamPositions(
					tag.Phase, _streams.ToDictionary(v => v, v => tag.Streams.TryGetValue(v, out p) ? p : -1));
			case CheckpointTag.Mode.PreparePosition:
				throw new NotSupportedException(
					"Conversion from PreparePosition to MultiStream position tag is not supported");
			case CheckpointTag.Mode.Position:
				throw new NotSupportedException(
					"Conversion from Position to MultiStream position tag is not supported");
			default:
				throw new NotSupportedException(string.Format(
					"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {0}",
					tag.ToString()));
		}
	}
}
