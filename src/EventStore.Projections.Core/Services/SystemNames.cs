// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Services;

public static class ProjectionEventTypes {
	public const string ProjectionCheckpoint = "$ProjectionCheckpoint";
	public const string PartitionCheckpoint = "$Checkpoint";
	public const string StreamTracked = "$StreamTracked";

	public const string ProjectionCreated = "$ProjectionCreated";
	public const string ProjectionDeleted = "$ProjectionDeleted";
	public const string ProjectionsInitialized = "$ProjectionsInitialized";
	public const string ProjectionUpdated = "$ProjectionUpdated";
}
