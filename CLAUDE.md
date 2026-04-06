# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
Detailed reference docs live in `.claude/docs/` — fetch them when working in a specific area.

## Quick Reference: Where to Look

| Working on... | Read |
|---|---|
| Core infrastructure, project layout | `.claude/docs/architecture.md` |
| API v2 services | `.claude/docs/api-v2-patterns.md` |
| Writing tests | `.claude/docs/testing.md` |
| Protocol buffers, gRPC | `.claude/docs/protocol-v2.md` |
| Message bus, enumerators, authorization, indexes | `.claude/docs/patterns-and-conventions.md` |

## Development Commands

### Build
- `dotnet build -c Release /p:Platform=x64 --framework=net10.0 src/KurrentDB.sln`

### Test
- `dotnet test src/KurrentDB.sln` - Run all tests
- `dotnet test src/ProjectName.Tests/` - Run specific project
- `dotnet test --filter "FullyQualifiedName~TestMethodName"` - Run single test

### Development Server
- `dotnet ./src/KurrentDB/bin/Release/net10.0/KurrentDB.dll --dev --db ./tmp/data --index ./tmp/index --log ./tmp/log`
- HTTP/gRPC: 2113, Internal TCP: 1112
- Admin UI: `http://localhost:2113` | Legacy: `http://localhost:2113/web`

## Architecture (Summary)

KurrentDB is an event-native database. .NET 10.0, 90+ projects, plugin-based architecture.

**Key areas**: Core engine, Plugin system, API v2, Projections (V1 + V2), Secondary Indexing (DuckDB), Schema Registry, Connectors. See `.claude/docs/architecture.md` for full details.

### Active Development Areas

- **API v2** (`src/KurrentDB.Api.V2/`) — next-gen API, evolving rapidly. See `.claude/docs/api-v2-patterns.md`
- **Projections V2** (`src/KurrentDB.Projections.V2/`) — next-gen projection engine with partitioned processing
- **Schema Registry** (`src/SchemaRegistry/`) — event validation and schema management
- **Secondary Indexing** (`src/KurrentDB.SecondaryIndexing/`) — DuckDB-backed query optimization

### Projections V2 Engine

Key components and their threading model:
- `ProjectionEngineV2`: Main read loop, dispatches events to partitions, triggers checkpoints
- `PartitionDispatcher`: Routes events to partition channels by hash of partition key
- `PartitionProcessor`: Processes events within a single partition, manages state and output buffers
- `CheckpointCoordinator`: Chandy-Lamport style — collects frozen buffers from all partitions, writes atomic multi-stream checkpoint
- `CoreProjectionV2`: Adapter implementing `ICoreProjectionControl` — **runs on projection worker queue, NOT thread safe**
- `ProjectionProcessingStrategyV2`: Factory creating V2 engines

Uses `ISystemClient` for writes (not raw `IPublisher`), `IReadStrategy` for reads.

## Threading Model and Concurrency

**Always ask: "What thread does this run on? Is that safe?"**

### Critical Threading Rules

- **Projection worker queue**: `CoreProjection`, `CoreProjectionV2` — NOT thread safe. Never call from thread pool.
- **Bus dispatch thread**: `CallbackEnvelope` callbacks run here. **Never use `CallbackEnvelope` + `TaskCompletionSource` to await a response** — use `TcsEnvelope<T>` instead (uses `RunContinuationsAsynchronously`).
- **Fire-and-forget tasks**: `_ = SomeAsync()` must not call back into non-thread-safe objects.

### DotNext Threading Utilities

- `TcsEnvelope<T>`: Safe async await for bus responses
- `AsyncExclusiveLock`: Ensure only one operation in flight (e.g., checkpointing)
- `[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]`: Start async method on new thread, don't capture caller's sync context. Use for background loops.

### Lifecycle Rules

- The component that creates a `CancellationTokenSource` owns it (cancel + dispose).
- If a component can be stopped and restarted, stop must complete before start begins.
- Prefer `Run(checkpoint, ct)` over separate `Start()`/`Stop()` when lifecycle is a single run.
- When checkpointing from multiple partitions, ensure only one checkpoint in flight at a time.

## C# Coding Conventions

### Parameter Design — Non-Optional, No Fallbacks

```csharp
// WRONG: silent fallback hides a bug
public Foo(IPublisher publisher, IPublisher mainBus = null) {
    _mainBus = mainBus ?? publisher;
}

// CORRECT
public Foo(IPublisher publisher, IPublisher mainBus) {
    _mainBus = mainBus;
}
```

- No `= null` on parameters that are actually required
- No `= 1` magic number defaults on version/config parameters
- Replace `?? fallbackValue` with `?? throw new InvalidOperationException("explanation")` when null is a bug
- Make types non-nullable (`T` not `T?`) when the value is always expected
- Always pass `ClaimsPrincipal` and `requireLeader` explicitly — never hardcode or default

### Naming

- **Versioned classes**: `NounVersionSuffix` — `CoreProjectionV2` (not `V2CoreProjection`)
- **Parameter accuracy**: `mainQueue` if it's a queue (not `mainBus`)
- **Named booleans**: `requireLeader: true` at call sites, not just `true`
- **Constants**: `ProjectionConstants.EngineV2` not `2`, `ExpectedVersion.Any` not `-2`

### Encapsulation

- Expose `IReadOnlyList<>`, `IReadOnlyDictionary<>` to consumers that only read
- Use `private init` on record properties set only by factory methods
- Pass factory instead of both value + factory

### Micro-Patterns

- `string.StartsWith('$')` (char) not `string.StartsWith("$")` (allocates)
- `string.GetHashCode()` over `XxHash32(Encoding.UTF8.GetBytes(...))` when hash stability isn't needed
- `[]` collection expression over `new Dictionary<K,V>()`
- Don't put `IAsyncDisposable` on interfaces when disposal is an implementation concern

### Abstraction Discipline

- Two classes doing the same thing with different config? Merge — pass the config as a parameter.
- Interface adding lifecycle (`IAsyncDisposable`) that's the implementation's concern? Simplify the interface.
- Method takes both a value and a factory-for-that-value? Just use the factory.

## Log Level Policy

| Level | When |
|---|---|
| **Verbose** | Per-event, per-message — anything that fires per-record |
| **Debug** | Checkpoint writes, per-batch operations |
| **Information** | Engine start/stop, lifecycle transitions only |
| **Warning** | Recoverable errors, degraded states |
| **Error** | Unrecoverable failures |

Per-event or per-checkpoint log messages must NOT be `Information`.

## MCP Servers

- **Microsoft Docs**: `microsoft_docs_search`, `microsoft_docs_fetch` — query for .NET/Azure docs
- **Context7**: Library documentation and code examples
- Config: `.claude/settings.json` (committed), `.claude/settings.local.json` (local)

## Configuration Defaults (v25.1)

- **SecondaryIndexing:Enabled** — `true`
- **ServerGC** — `true`
- **StreamInfoCacheCapacity** — `100000`
- **MemDb** — Deprecated, will be removed
