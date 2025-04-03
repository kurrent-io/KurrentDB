// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Management.ManagedProjectionStates;

class StartingState : ManagedProjectionStateBase {
	public StartingState(ManagedProjection managedProjection)
		: base(managedProjection) {
	}

	protected internal override void Started() {
		_managedProjection.SetState(ManagedProjectionState.Running);
		_managedProjection.StartCompleted();
	}

	protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
		SetFaulted(message.FaultedReason);
		_managedProjection.StartCompleted();
	}
}
