// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace KurrentDB.Surge.Testing.Fixtures;

public partial class FastFixture {
    public string NewStreamId([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();

    public string GenerateShortId()  => Guid.NewGuid().ToString()[30..];
    public string NewConnectorId()   => $"connector-id-{GenerateShortId()}".ToLowerInvariant();
    public string NewConnectorName() => $"connector-name-{GenerateShortId()}".ToLowerInvariant();
}