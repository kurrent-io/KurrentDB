// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Plugins.Subsystems;

namespace EventStore.POC.IO.Core;

// Necessary for now because of how PluginLoader works.
// Later it may be possible to remove this interface entirely.
public interface IConnectedSubsystemsPlugin {
	string Name { get; }
	string Version { get; }
	string CommandLineName { get; }
	IReadOnlyList<ISubsystem> GetSubsystems();
}
