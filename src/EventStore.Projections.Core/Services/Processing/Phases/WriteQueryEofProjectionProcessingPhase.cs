// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public sealed class WriteQueryEofProjectionProcessingPhase : WriteQueryResultProjectionProcessingPhaseBase {
	public WriteQueryEofProjectionProcessingPhase(
		IPublisher publisher,
		int phase,
		string resultStream,
		ICoreProjectionForProcessingPhase coreProjection,
		PartitionStateCache stateCache,
		ICoreProjectionCheckpointManager checkpointManager,
		IEmittedEventWriter emittedEventWriter,
		IEmittedStreamsTracker emittedStreamsTracker)
		: base(publisher, phase, resultStream, coreProjection, stateCache, checkpointManager, emittedEventWriter,
			emittedStreamsTracker) {
	}

	protected override IEnumerable<EmittedEventEnvelope> WriteResults(CheckpointTag phaseCheckpointTag) {
		// do nothing
		yield break;
	}
}
