// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Scavenging;

public struct Unit : IEquatable<Unit> {
	public static readonly Unit Instance = new Unit();

	public override int GetHashCode() => 1;
	public override bool Equals(object other) => other is Unit;
	public bool Equals(Unit other) => true;
}
