// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Services.Management.ManagedProjectionStates;

class PreparedState : ManagedProjectionStateBase {
	public PreparedState(ManagedProjection managedProjection)
		: base(managedProjection) {
	}
}
