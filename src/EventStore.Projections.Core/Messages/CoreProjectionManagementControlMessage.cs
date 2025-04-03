// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messages;

[DerivedMessage]
public abstract partial class CoreProjectionManagementControlMessage : CoreProjectionManagementMessageBase {
	private readonly Guid _workerId;

	public Guid WorkerId {
		get { return _workerId; }
	}

	public CoreProjectionManagementControlMessage(Guid projectionId, Guid workerId)
		: base(projectionId) {
		_workerId = workerId;
	}
}
