// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;

namespace KurrentDB.Projections.Core.Messages;

/// <summary>
/// Interface for the management layer to control a projection instance.
/// Both V1 (CoreProjection) and V2 (V2CoreProjection) implement this contract.
/// </summary>
public interface ICoreProjectionControl :
	IHandle<CoreProjectionManagementMessage.GetState>,
	IHandle<CoreProjectionManagementMessage.GetResult>,
	ICoreProjection,
	IDisposable {
	Guid ProjectionCorrelationId { get; }
	void Start();
	void Stop();
	void Kill();
	void LoadStopped();
	bool Suspend();
}
