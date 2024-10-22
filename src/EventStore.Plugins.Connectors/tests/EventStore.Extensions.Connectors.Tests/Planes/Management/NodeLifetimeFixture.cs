// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Runtime.CompilerServices;
using EventStore.Toolkit.Testing.Fixtures;

namespace EventStore.Connectors.Tests.Management;

public class NodeLifetimeFixture : FastFixture {
    public string NewIdentifier([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();
}