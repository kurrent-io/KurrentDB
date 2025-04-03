// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Management.ManagedProjectionStates;

class RunningState : ManagedProjectionStateBase {
	public RunningState(ManagedProjection managedProjection)
		: base(managedProjection) {
	}

	protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
		SetFaulted(message.FaultedReason);
	}

	protected internal override void Started() {
		// do nothing - may mean second pahse started
		//TODO: stop sending second Started
	}

	protected internal override void Stopped(CoreProjectionStatusMessage.Stopped message) {
		if (message.Completed)
			_managedProjection.SetState(ManagedProjectionState.Completed);
		else
			base.Stopped(message);
	}
}
