// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using TUnit.Core.Interfaces;

namespace KurrentDB.Api.Tests.Infrastructure;

public sealed record AppendStampedeLimit : IParallelLimit {
    // Every repeat of append_session_throws_when_transaction_is_too_large pushes ~30 MB at the node
    // and they all share one GrpcChannel. Measured on a 12-core host: 8 concurrent repeats pass
    // 51/51, 12 concurrent time out 48/51 against the assembly-wide 20s test timeout. The cap
    // protects each repeat's latency budget, so it must never exceed the core count either.
    public int Limit => Math.Min(8, Environment.ProcessorCount);
}
