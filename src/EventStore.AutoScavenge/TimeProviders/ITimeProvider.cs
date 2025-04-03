// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.TimeProviders;

/// <summary>
/// Time abstraction providing the current date.
/// </summary>
public interface ITimeProvider {
	DateTime Now { get; }
}
