// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messages;

[DerivedMessage]
public abstract partial class CoreProjectionManagementMessageBase : Message {
	private readonly Guid _projectionIdId;

	protected CoreProjectionManagementMessageBase(Guid projectionId) {
		_projectionIdId = projectionId;
	}

	public Guid ProjectionId {
		get { return _projectionIdId; }
	}
}
