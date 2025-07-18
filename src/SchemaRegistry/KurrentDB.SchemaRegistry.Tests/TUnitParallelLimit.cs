// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SchemaRegistry.Tests;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<SchemaRegistryParallelLimit>]

namespace KurrentDB.SchemaRegistry.Tests;

public record SchemaRegistryParallelLimit : IParallelLimit {
    // DuckDB is not thread-safe, so we need to limit the number of parallel tests
    public int Limit => 1; // Environment.ProcessorCount / 2;
}