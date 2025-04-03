// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.Tests;

public class SimulatorCommands {
	public record FastForward(TimeSpan Time) : Command<Unit>(_ => { });

	public record Suspend() : Command<Unit>(_ => { });
}
