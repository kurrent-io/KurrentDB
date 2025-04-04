// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

public class CheckpointWorkItemBase : WorkItem {
	private static readonly object _correlationId = new object();

	protected CheckpointWorkItemBase()
		: base(_correlationId) {
		_requiresRunning = true;
	}

	protected CheckpointWorkItemBase(object correlation)
		: base(correlation) {
	}
}
