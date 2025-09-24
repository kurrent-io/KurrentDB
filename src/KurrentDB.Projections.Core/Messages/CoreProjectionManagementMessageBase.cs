// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Projections.Core.Messages;

[DerivedMessage]
public abstract partial class CoreProjectionManagementMessageBase(Guid projectionId) : Message {
	public Guid ProjectionId { get; } = projectionId;
}
