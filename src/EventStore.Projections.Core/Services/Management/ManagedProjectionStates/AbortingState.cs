// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Management.ManagedProjectionStates;

class AbortingState : ManagedProjectionStateBase {
	public AbortingState(ManagedProjection managedProjection)
		: base(managedProjection) {
	}

	protected internal override void Stopped(CoreProjectionStatusMessage.Stopped message) {
		_managedProjection.SetState(ManagedProjectionState.Aborted);
		_managedProjection.PrepareOrWriteStartOrLoadStopped();
	}

	protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
		_managedProjection.SetState(ManagedProjectionState.Aborted);
		_managedProjection.PrepareOrWriteStartOrLoadStopped();
	}
}
