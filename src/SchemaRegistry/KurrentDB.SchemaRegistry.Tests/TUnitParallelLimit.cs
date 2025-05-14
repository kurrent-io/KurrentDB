using KurrentDB.SchemaRegistry.Tests;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<SchemaRegistryParallelLimit>]

namespace KurrentDB.SchemaRegistry.Tests;

public record SchemaRegistryParallelLimit : IParallelLimit {
    // DuckDB is not thread-safe, so we need to limit the number of parallel tests
    public int Limit => 1; // Environment.ProcessorCount / 2;
}