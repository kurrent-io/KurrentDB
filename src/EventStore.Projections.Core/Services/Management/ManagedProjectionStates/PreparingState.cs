// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Management.ManagedProjectionStates;

class PreparingState : ManagedProjectionStateBase {
	public PreparingState(ManagedProjection managedProjection)
		: base(managedProjection) {
	}

	protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
		SetFaulted(message.FaultedReason);
		_managedProjection.PersistedProjectionState.SourceDefinition = null;
		_managedProjection.WriteStartOrLoadStopped();
	}

	protected internal override void Prepared(CoreProjectionStatusMessage.Prepared message) {
		_managedProjection.SetState(ManagedProjectionState.Prepared);
		_managedProjection.PersistedProjectionState.SourceDefinition = message.SourceDefinition;
		_managedProjection.Prepared = true;
		_managedProjection.Created = true;
		_managedProjection.WriteStartOrLoadStopped();
	}
}
