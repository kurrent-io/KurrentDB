// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FASTER.core;

namespace EventStore.Core.LogV3.FASTER;

public class Context<TValue> {
	public Status Status { get; set; }
	public TValue Value { get; set; }
}

