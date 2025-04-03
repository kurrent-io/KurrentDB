// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;

namespace EventStore.AutoScavenge.Tests;

public record Snapshot(
	IEvent Event,
	AutoScavengeState PrevState,
	AutoScavengeState NewState,
	DateTime Time);

