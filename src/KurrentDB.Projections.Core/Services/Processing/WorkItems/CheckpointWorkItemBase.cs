// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

public class CheckpointWorkItemBase : WorkItem {
	private static readonly object CorrelationId = new();

	protected CheckpointWorkItemBase() : base(CorrelationId) {
		RequiresRunning = true;
	}

	protected CheckpointWorkItemBase(object correlation) : base(correlation) {
	}
}
