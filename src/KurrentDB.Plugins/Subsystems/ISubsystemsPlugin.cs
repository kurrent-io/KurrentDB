// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Subsystems;

/// <summary>
///     A plugin that can create multiple subsystems.
/// </summary>
public interface ISubsystemsPlugin {
	string Name { get; }
	string Version { get; }
	string CommandLineName { get; }

	IReadOnlyList<ISubsystem> GetSubsystems();
}
