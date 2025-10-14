// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Authorization;

public readonly record struct Parameter(string Name, string Value) {
	public override string ToString() => $"{Name} : {Value}";
}
