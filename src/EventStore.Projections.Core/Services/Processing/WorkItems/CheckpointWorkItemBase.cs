// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
