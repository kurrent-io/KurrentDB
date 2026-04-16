// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Services;

public static class ProjectionEventTypes {
	// V1 & V2
	public const string ProjectionCheckpoint = "$ProjectionCheckpoint";

	// V1 only
	public const string PartitionCheckpoint = "$Checkpoint";
	public const string StreamTracked = "$StreamTracked";

	// V2 only
	public const string ProjectionState = "$ProjectionState.V2";

	// Management
	public const string ProjectionCreated = "$ProjectionCreated";
	public const string ProjectionDeleted = "$ProjectionDeleted";
	public const string ProjectionsInitialized = "$ProjectionsInitialized";
	public const string ProjectionUpdated = "$ProjectionUpdated";
}
