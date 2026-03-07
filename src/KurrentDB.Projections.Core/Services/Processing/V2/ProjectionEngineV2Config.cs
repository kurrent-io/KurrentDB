// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class ProjectionEngineV2Config {
	public required string ProjectionName { get; init; }
	public required IQuerySources SourceDefinition { get; init; }
	public required IProjectionStateHandler StateHandler { get; init; }
	public int PartitionCount { get; init; } = 4;
	public int CheckpointAfterMs { get; init; } = 2000;
	public int CheckpointHandledThreshold { get; init; } = 4000;
	public long CheckpointUnhandledBytesThreshold { get; init; } = 10_000_000;
	public bool EmitEnabled { get; init; }
	public bool TrackEmittedStreams { get; init; }
}
