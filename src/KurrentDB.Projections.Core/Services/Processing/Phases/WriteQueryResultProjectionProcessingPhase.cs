// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public sealed class WriteQueryResultProjectionProcessingPhase(
	IPublisher publisher,
	int phase,
	string resultStream,
	ICoreProjectionForProcessingPhase coreProjection,
	PartitionStateCache stateCache,
	ICoreProjectionCheckpointManager checkpointManager,
	IEmittedEventWriter emittedEventWriter,
	IEmittedStreamsTracker emittedStreamsTracker)
	: WriteQueryResultProjectionProcessingPhaseBase(publisher, phase, resultStream, coreProjection, stateCache, checkpointManager,
		emittedEventWriter,
		emittedStreamsTracker) {
	protected override IEnumerable<EmittedEventEnvelope> WriteResults(CheckpointTag phaseCheckpointTag) {
		var items = StateCache.Enumerate();
		return items.Select(t => new EmittedEventEnvelope(
			new EmittedDataEvent(ResultStream, Guid.NewGuid(), "Result", true, t.State.Result, null, phaseCheckpointTag, null)
		));
	}
}
