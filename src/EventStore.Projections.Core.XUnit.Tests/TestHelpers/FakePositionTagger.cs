// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.XUnit.Tests.TestHelpers;

// TODO: Flesh out this helper as more tests need it
public class FakePositionTagger : PositionTagger {
	public FakePositionTagger(int phase) : base(phase) {
	}

	public override bool IsMessageAfterCheckpointTag(CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionEof partitionEof) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeCheckpointTag(CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		throw new NotImplementedException();
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.Empty;
	}

	public override bool IsCompatible(CheckpointTag checkpointTag) {
		throw new NotImplementedException();
	}

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		return tag;
	}
}

