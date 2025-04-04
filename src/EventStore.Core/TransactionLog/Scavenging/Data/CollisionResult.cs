// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Scavenging;

public enum CollisionResult {
	NoCollision,
	NewCollision, // collided with something that was not previously in a collision
	OldCollision, // collided with something that was already in a collision
}
