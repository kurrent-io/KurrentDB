// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

class NotAuthorizedWorkItem : CheckpointWorkItemBase {
	public NotAuthorizedWorkItem()
		: base(null) {
	}

	protected override void ProcessEvent() {
		throw new Exception("Projection cannot read its source. Not authorized.");
	}
}
