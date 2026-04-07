// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public class NoopResultEventEmitter : IResultEventEmitter {
	public EmittedEventEnvelope[] ResultUpdated(string partition, string result, CheckpointTag at) {
		throw new NotSupportedException("No results are expected from the projection");
	}
}
