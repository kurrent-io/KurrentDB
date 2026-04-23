# Projections V2 Memory Control — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two unbounded partition-state caches in Projections V2 with bounded SIEVE caches backed by `DotNext.Runtime.Caching.RandomAccessCache`, configurable via a system-wide `MaxPartitionStateCacheSize` option.

**Architecture:** One internal wrapper type `PartitionStateCache` owns DotNext session lifetimes, exposes `TryGet`/`Set`, and tracks `Evictions`/`Count`. Each `PartitionProcessor` holds its own instance; `ProjectionEngineV2` holds one shared instance. A single capacity (`MaxPartitionStateCacheSize`) flows from `ClusterVNodeOptions.Projection` through `ProjectionSubsystemOptions` → `ProjectionsStandardComponents` → `ProjectionCoreService` → `ProjectionProcessingStrategyV2` → `ProjectionEngineV2Config`. Eviction is silent — both caches already have stream-fallback paths. Stats (`ProjectionStatistics`) gains `PartitionStateCacheEvictions` and `PartitionStateCacheSize` fields, populated from the engine.

**Tech Stack:** .NET 10, C# 14, DotNext 6.1.0 (`DotNext.Runtime.Caching.RandomAccessCache<TKey,TValue>`), TUnit tests, Serilog.

## Build & Test Commands (local macOS ARM64)

CLAUDE.md documents `/p:Platform=x64 --framework=net10.0`, but on an ARM64 Mac the x64-platform source-generator DLL can't load into the native-ARM64 `dotnet` host (CS8034). Parallel MSBuild also hits transient `StaticWebAssets` cache-file contention on this host. For the commands in this plan, use:

- **Platform:** `/p:Platform=ARM64` (not `x64`).
- **Parallelism:** `--maxcpucount:1` on `dotnet build` (tests are fine without).
- **Restore:** run `dotnet restore src/KurrentDB.sln` once before the first build in the worktree (reuses the existing baseline for subsequent tasks).

CI and x64 hosts keep the CLAUDE.md-documented flags; this is a local-only override.

---

## File Structure

**New files:**

- `src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionStateCache.cs` — cache wrapper.
- `src/KurrentDB.Projections.V2.Tests/Unit/PartitionStateCacheTests.cs` — wrapper unit tests.
- `src/KurrentDB.Projections.V2.Tests/Integration/PartitionStateCacheEvictionTests.cs` — engine-level integration tests for eviction + stream fallback.

**Modified files:**

- `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2Config.cs` — add `MaxPartitionStateCacheSize`.
- `src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionProcessor.cs` — swap Dictionary for wrapper; add ctor param; dispose in `Run`'s finally.
- `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2.cs` — swap ConcurrentDictionary for wrapper; expose `GetCacheMetrics()`; dispose shared cache.
- `src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs` — populate new stats fields; set `MaxPartitionStateCacheSize` on `ProjectionEngineV2Config`.
- `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionProcessingStrategyV2.cs` — accept `maxPartitionStateCacheSize` ctor param; thread to `CoreProjectionV2`.
- `src/KurrentDB.Projections.Management/Services/Processing/Strategies/ProcessingStrategySelector.cs` — accept, pass through.
- `src/KurrentDB.Projections.Management/Services/Processing/ProjectionCoreService.cs` — read from `ProjectionsStandardComponents`, pass to selector.
- `src/KurrentDB.Projections.Shared/ProjectionsStandardComponents.cs` — add `MaxPartitionStateCacheSize`.
- `src/KurrentDB.Projections.Management/ProjectionsSubsystem.cs` — add `MaxPartitionStateCacheSize` to `ProjectionSubsystemOptions` record and class; pass to `ProjectionsStandardComponents`.
- `src/KurrentDB/ClusterVNodeHostedService.cs` — pass new option into `ProjectionSubsystemOptions`.
- `src/KurrentDB.Core/Configuration/ClusterVNodeOptions.cs` — add `MaxPartitionStateCacheSize` to `ProjectionOptions` record.
- `src/KurrentDB.Projections.Shared/Services/ProjectionStatistics.cs` — add `PartitionStateCacheEvictions`, `PartitionStateCacheSize`.

**Task ordering rationale:** build the wrapper first (self-contained, TDD), then swap the two call sites (wiring still uses the default `100_000` in the config until operator plumbing lands), then plumb the operator knob through the subsystem in one sweep, then add stats, then integration-test the full path.

---

### Task 1: Create `PartitionStateCache` wrapper (TDD)

**Files:**

- Create: `src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionStateCache.cs`
- Create: `src/KurrentDB.Projections.V2.Tests/Unit/PartitionStateCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/KurrentDB.Projections.V2.Tests/Unit/PartitionStateCacheTests.cs`:

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using KurrentDB.Projections.Core.Services.Processing.V2;

namespace KurrentDB.Projections.V2.Tests.Unit;

public class PartitionStateCacheTests {
	[Test]
	public async Task try_get_returns_false_for_missing_key() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		var hit = cache.TryGet("missing", out var value);

		await Assert.That(hit).IsFalse();
		await Assert.That(value).IsNull();
	}

	[Test]
	public async Task set_then_try_get_returns_value() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		await cache.Set("k", "v1", CancellationToken.None);

		var hit = cache.TryGet("k", out var value);
		await Assert.That(hit).IsTrue();
		await Assert.That(value).IsEqualTo("v1");
	}

	[Test]
	public async Task set_overwrites_existing_value() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		await cache.Set("k", "v1", CancellationToken.None);
		await cache.Set("k", "v2", CancellationToken.None);

		cache.TryGet("k", out var value);
		await Assert.That(value).IsEqualTo("v2");
		await Assert.That(cache.Count).IsEqualTo(1L);
	}

	[Test]
	public async Task null_value_is_preserved() {
		await using var cache = new PartitionStateCache(capacity: 4, name: "test", projectionName: "p");

		await cache.Set("k", null, CancellationToken.None);

		var hit = cache.TryGet("k", out var value);
		await Assert.That(hit).IsTrue();
		await Assert.That(value).IsNull();
	}

	[Test]
	public async Task exceeding_capacity_evicts_and_counter_increments() {
		const int capacity = 4;
		await using var cache = new PartitionStateCache(capacity, name: "test", projectionName: "p");

		for (var i = 0; i < capacity + 3; i++)
			await cache.Set($"k{i}", $"v{i}", CancellationToken.None);

		await Assert.That(cache.Count).IsLessThanOrEqualTo((long)capacity);
		await Assert.That(cache.Evictions).IsGreaterThanOrEqualTo(3L);
	}

	[Test]
	public async Task concurrent_reads_and_writes_do_not_throw() {
		await using var cache = new PartitionStateCache(capacity: 64, name: "test", projectionName: "p");

		var writers = Enumerable.Range(0, 8).Select(w => Task.Run(async () => {
			for (var i = 0; i < 200; i++)
				await cache.Set($"w{w}-k{i % 16}", $"v{i}", CancellationToken.None);
		}));

		var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
			for (var i = 0; i < 500; i++)
				cache.TryGet($"w{i % 8}-k{i % 16}", out _);
		}));

		await Task.WhenAll(writers.Concat(readers));
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ --filter "FullyQualifiedName~PartitionStateCacheTests" -c Release /p:Platform=ARM64`

Expected: compile failure — `PartitionStateCache` not found.

- [ ] **Step 3: Implement `PartitionStateCache`**

Create `src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionStateCache.cs`:

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.Caching;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

internal sealed class PartitionStateCache : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionStateCache>();

	private readonly RandomAccessCache<string, string?> _cache;
	private readonly string _name;
	private readonly string _projectionName;
	private long _evictions;
	private long _count;

	public PartitionStateCache(int capacity, string name, string projectionName) {
		if (capacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive");
		_name = name;
		_projectionName = projectionName;
		_cache = new RandomAccessCache<string, string?>(capacity) {
			Eviction = OnEviction,
			KeyComparer = StringComparer.Ordinal,
		};
	}

	public long Evictions => Interlocked.Read(ref _evictions);
	public long Count => Interlocked.Read(ref _count);

	public bool TryGet(string key, out string? value) {
		if (_cache.TryRead(key, out var session)) {
			using (session) {
				value = session.Value;
				return true;
			}
		}

		value = null;
		return false;
	}

	public async ValueTask Set(string key, string? value, CancellationToken ct) {
		using var session = await _cache.ChangeAsync(key, ct).ConfigureAwait(false);
		var isNew = !session.TryGetValue(out _);
		session.SetValue(value);
		if (isNew)
			Interlocked.Increment(ref _count);
	}

	public ValueTask DisposeAsync() => _cache.DisposeAsync();

	private void OnEviction(string key, string? _) {
		Interlocked.Increment(ref _evictions);
		Interlocked.Decrement(ref _count);
		Log.Verbose("Partition state cache {Cache} for projection {Projection} evicted partition {Partition}",
			_name, _projectionName, key);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ --filter "FullyQualifiedName~PartitionStateCacheTests" -c Release /p:Platform=ARM64`

Expected: all 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionStateCache.cs \
        src/KurrentDB.Projections.V2.Tests/Unit/PartitionStateCacheTests.cs
git commit -m "$(cat <<'EOF'
feat(projections-v2): introduce PartitionStateCache wrapper

Wraps DotNext RandomAccessCache with TryGet/Set surface plus Evictions
and Count counters. Eviction callback tracks the counter and emits a
Verbose log. Used by PartitionProcessor and ProjectionEngineV2 in
follow-up commits.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add `MaxPartitionStateCacheSize` to `ProjectionEngineV2Config`

**Files:**

- Modify: `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2Config.cs`

- [ ] **Step 1: Add the required property**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2Config.cs`. Replace the class body so it reads:

```csharp
public class ProjectionEngineV2Config {
	public required string ProjectionName { get; init; }
	public required IQuerySources SourceDefinition { get; init; }
	public required Func<IProjectionStateHandler> StateHandlerFactory { get; init; }
	public required int MaxPartitionStateCacheSize { get; init; }
	public int PartitionCount { get; init; } = 4;
	public int CheckpointAfterMs { get; init; } = 2000;
	public int CheckpointHandledThreshold { get; init; } = 4000;
	public long CheckpointUnhandledBytesThreshold { get; init; } = 10_000_000;
	public bool EmitEnabled { get; init; }
}
```

Making it `required` ensures every construction site is forced to set a value (per the project's convention on non-optional parameters).

- [ ] **Step 2: Build — expect failures**

Run: `dotnet build -c Release /p:Platform=ARM64 --framework=net10.0 --maxcpucount:1 src/KurrentDB.sln 2>&1 | grep -E 'error|Error' | head -20`

Expected: a compile error at `CoreProjectionV2.StartEngine` (the only current construction site) because `MaxPartitionStateCacheSize` is now required. This is intentional — subsequent tasks provide the value.

- [ ] **Step 3: Set a temporary default at the single call site**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs` and locate `StartEngine`. Add the new property inside the `ProjectionEngineV2Config` initializer:

```csharp
var config = new ProjectionEngineV2Config {
	ProjectionName = _projectionName,
	SourceDefinition = _sourceDefinition,
	StateHandlerFactory = _stateHandlerFactory,
	MaxPartitionStateCacheSize = 100_000, // TEMP: replaced with wired value in a later task
	CheckpointAfterMs = _projectionConfig.CheckpointAfterMs,
	CheckpointHandledThreshold = _projectionConfig.CheckpointHandledThreshold,
	CheckpointUnhandledBytesThreshold = _projectionConfig.CheckpointUnhandledBytesThreshold,
	EmitEnabled = _projectionConfig.EmitEventEnabled
};
```

Leave the `// TEMP:` comment in place; the wiring task removes it.

- [ ] **Step 4: Build to confirm green**

Run: `dotnet build -c Release /p:Platform=ARM64 --framework=net10.0 --maxcpucount:1 src/KurrentDB.sln 2>&1 | tail -5`

Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2Config.cs \
        src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs
git commit -m "$(cat <<'EOF'
feat(projections-v2): add MaxPartitionStateCacheSize to engine config

Required init property; temporary 100_000 literal at the one
construction site until the operator knob is plumbed through the
subsystem in a follow-up commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Swap `PartitionProcessor._stateCache` for `PartitionStateCache`

**Files:**

- Modify: `src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionProcessor.cs`

- [ ] **Step 1: Update constructor parameters and fields**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionProcessor.cs`. Replace the primary constructor signature, the shared-cache parameter type, and the `_stateCache` field:

```csharp
public class PartitionProcessor(
	int partitionIndex,
	ChannelReader<PartitionEvent> reader,
	IProjectionStateHandler stateHandler,
	string projectionName,
	bool isBiState,
	bool emitEnabled,
	Action<int, IReadOnlyOutputBuffer> onCheckpointMarker,
	Func<string, ValueTask<string?>> loadPersistedState,
	PartitionStateCache sharedPartitionStates,
	int partitionStateCacheCapacity) {

	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionProcessor>();

	private OutputBuffer _activeBuffer = new();
	private OutputBuffer _frozenBuffer = new();
	private readonly PartitionStateCache _stateCache =
		new(partitionStateCacheCapacity, name: $"partition-{partitionIndex}", projectionName);
	private string? _sharedState;
	private bool _sharedStateInitialized;
```

Remove the `using System.Collections.Concurrent;` import (no longer needed here) and the old `Dictionary<string, string?> _stateCache` field.

- [ ] **Step 2: Update `LoadPartitionState` to use the cache**

Replace the body of `LoadPartitionState`:

```csharp
private async ValueTask<bool> LoadPartitionState(string partitionKey) {
	if (_stateCache.TryGet(partitionKey, out var cachedState)) {
		stateHandler.Load(cachedState ?? "null");
		return false;
	}

	var persistedState = await loadPersistedState(partitionKey);
	if (persistedState is not null) {
		Log.Debug("Loaded persisted state for partition {Partition} in projection {Name}",
			partitionKey, projectionName);
		stateHandler.Load(persistedState);
		await _stateCache.Set(partitionKey, persistedState, CancellationToken.None);
		return false;
	}

	stateHandler.Initialize();
	return true;
}
```

- [ ] **Step 3: Update `ProcessEvent` and `ProcessPartitionDeleted` writes**

In `ProcessPartitionDeleted`, replace `_stateCache[partitionKey] = newState;` with:

```csharp
await _stateCache.Set(partitionKey, newState, CancellationToken.None);
```

And replace `sharedPartitionStates[partitionKey] = newState;` with:

```csharp
await sharedPartitionStates.Set(partitionKey, newState, CancellationToken.None);
```

Do the same in `ProcessEvent` (both inside the `if (processed)` block).

- [ ] **Step 4: Dispose the per-slot cache on exit**

Replace the `Run` method:

```csharp
public async Task Run(CancellationToken ct) {
	Log.Debug("Partition {Index} starting for projection {Name}", partitionIndex, projectionName);

	try {
		await foreach (var pe in reader.ReadAllAsync(ct)) {
			if (pe.IsCheckpointMarker)
				HandleCheckpointMarker();
			else if (pe.IsPartitionDeleted)
				await ProcessPartitionDeleted(pe);
			else
				await ProcessEvent(pe);
		}
	} finally {
		await _stateCache.DisposeAsync();
	}
}
```

- [ ] **Step 5: Build**

Run: `dotnet build -c Release /p:Platform=ARM64 --framework=net10.0 --maxcpucount:1 src/KurrentDB.sln 2>&1 | tail -10`

Expected: compile errors in `ProjectionEngineV2.cs` (shared-cache type mismatch) and any test using the old `PartitionProcessor` constructor — fixed in the next task.

- [ ] **Step 6: Commit**

```bash
git add src/KurrentDB.Projections.V2/Services/Processing/V2/PartitionProcessor.cs
git commit -m "$(cat <<'EOF'
feat(projections-v2): swap PartitionProcessor state cache to PartitionStateCache

Per-partition state cache and shared partition states now flow through
the bounded PartitionStateCache wrapper. Per-slot cache is disposed in
Run's finally so the engine can rely on drain completion.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Swap `ProjectionEngineV2._partitionStates` for `PartitionStateCache`; expose metrics

**Files:**

- Modify: `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2.cs`

- [ ] **Step 1: Swap field type and construction**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2.cs`.

Remove `using System.Collections.Concurrent;`.

Replace the `_partitionStates` field:

```csharp
private readonly PartitionStateCache _partitionStates;
```

Initialise it in the constructor body (records keep the primary ctor; add a constructor body block). Replace the current ctor signature + field initialisers with:

```csharp
public sealed class ProjectionEngineV2 : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionEngineV2>();

	private readonly ProjectionEngineV2Config _config;
	private readonly IReadStrategy _readStrategy;
	private readonly ISystemClient _client;
	private readonly ClaimsPrincipal _user;
	private readonly CancellationTokenSource _cts = new();
	private readonly PartitionStateCache _partitionStates;
	private Task _runTask;
	private long _totalEventsProcessed;

	public ProjectionEngineV2(
		ProjectionEngineV2Config config,
		IReadStrategy readStrategy,
		ISystemClient client,
		ClaimsPrincipal user) {
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_readStrategy = readStrategy ?? throw new ArgumentNullException(nameof(readStrategy));
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_user = user ?? throw new ArgumentNullException(nameof(user));
		_partitionStates = new PartitionStateCache(
			_config.MaxPartitionStateCacheSize,
			name: "shared",
			projectionName: _config.ProjectionName);
	}
```

The remaining members (`Start`, `DisposeAsync`, `IsFaulted`, `IsStopped`, `IsStopping`, `FaultException`, `TotalEventsProcessed`, `GetPartitionState`, `Run`, `RunReadLoop`, `BuildPartitionKeyFunction`, `LoadPersistedPartitionState`, `ConvertToProjectionEvent`) stay.

- [ ] **Step 2: Update `GetPartitionState` to query the cache**

Replace the method body:

```csharp
public string GetPartitionState(string partitionKey) =>
	_partitionStates.TryGet(partitionKey, out var state) ? state : null;
```

(Note: `state` here is `string?`. The method signature already returns `string` nullable by convention — consumers null-check.)

- [ ] **Step 3: Pass the cache and capacity when constructing `PartitionProcessor`**

Inside `Run`, update the `PartitionProcessor` construction:

```csharp
var processor = new PartitionProcessor(
	i,
	dispatcher.GetPartitionReader(i),
	partitionHandlers[i],
	_config.ProjectionName,
	_config.SourceDefinition.IsBiState,
	_config.EmitEnabled,
	coordinator.ReportPartitionCheckpoint,
	loadPersistedState: partitionKey => LoadPersistedPartitionState(partitionKey, ct),
	sharedPartitionStates: _partitionStates,
	partitionStateCacheCapacity: _config.MaxPartitionStateCacheSize);
```

- [ ] **Step 4: Dispose the shared cache after partitions drain**

Replace `DisposeAsync`:

```csharp
public async ValueTask DisposeAsync() {
	if (_cts.IsCancellationRequested)
		return;

	await _cts.CancelAsync();
	if (_runTask is { } runTask)
		await runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

	await _partitionStates.DisposeAsync();
	_cts.Dispose();
}
```

Shared cache disposal sits after `runTask` completes, which in turn awaits `Task.WhenAll(partitionTasks)` inside `Run`'s finally — by the time we reach the shared dispose, no processor is still writing.

- [ ] **Step 5: Expose cache metrics**

Add a new method just above `GetPartitionState` (exact placement doesn't matter; adjacent to it is natural):

```csharp
public CacheMetrics GetCacheMetrics() =>
	new(Size: _partitionStates.Count, Evictions: _partitionStates.Evictions);

public readonly record struct CacheMetrics(long Size, long Evictions);
```

Note: per-slot cache metrics are not aggregated here. The shared cache sees every partition key ever touched (every write goes through it), so its size/evictions are a faithful summary of the engine's partition-state memory pressure. Per-slot caches are a hot-path read cache; omitting them from the operator-visible number avoids sum-of-unrelated-pools confusion.

- [ ] **Step 6: Build**

Run: `dotnet build -c Release /p:Platform=ARM64 --framework=net10.0 --maxcpucount:1 src/KurrentDB.sln 2>&1 | tail -10`

Expected: `Build succeeded`. Existing V2 tests that construct `ProjectionEngineV2Config` without `MaxPartitionStateCacheSize` will fail to compile — fix them inline by adding `MaxPartitionStateCacheSize = 100,` (or similar) to their config literals.

- [ ] **Step 7: Fix test fixtures that construct `ProjectionEngineV2Config`**

Run: `grep -rn "new ProjectionEngineV2Config" src/KurrentDB.Projections.V2.Tests/ --include="*.cs"`

For each match, add `MaxPartitionStateCacheSize = 1000,` to the initializer. Run the build again and confirm green.

- [ ] **Step 8: Run the V2 test suite**

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ -c Release /p:Platform=ARM64 --framework net10.0`

Expected: all existing tests pass (cache swap is behaviour-preserving at `1000` cap for tests that don't exercise >1000 distinct partitions).

- [ ] **Step 9: Commit**

```bash
git add src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionEngineV2.cs \
        src/KurrentDB.Projections.V2.Tests/
git commit -m "$(cat <<'EOF'
feat(projections-v2): swap ProjectionEngineV2 shared partition states to PartitionStateCache

Shared partition-state map becomes a bounded PartitionStateCache sized
by MaxPartitionStateCacheSize. Adds GetCacheMetrics() reporting the
shared cache's size and eviction count. Test fixtures updated to
populate the new required config property.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Wire `MaxPartitionStateCacheSize` from `ClusterVNodeOptions` to `ProjectionEngineV2Config`

**Files:**

- Modify: `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionProcessingStrategyV2.cs`
- Modify: `src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs`
- Modify: `src/KurrentDB.Projections.Management/Services/Processing/Strategies/ProcessingStrategySelector.cs`
- Modify: `src/KurrentDB.Projections.Management/Services/Processing/ProjectionCoreService.cs`
- Modify: `src/KurrentDB.Projections.Shared/ProjectionsStandardComponents.cs`
- Modify: `src/KurrentDB.Projections.Management/ProjectionsSubsystem.cs`
- Modify: `src/KurrentDB/ClusterVNodeHostedService.cs`
- Modify: `src/KurrentDB.Core/Configuration/ClusterVNodeOptions.cs`

- [ ] **Step 1: Add option to `ClusterVNodeOptions.ProjectionOptions`**

Open `src/KurrentDB.Core/Configuration/ClusterVNodeOptions.cs`. Inside the `public record ProjectionOptions` block, append:

```csharp
[Description("Maximum number of partition-state entries cached in memory per V2 projection cache " +
             "(one per partition slot plus a shared engine-wide cache). When the cap is reached, " +
             "entries are evicted LRU-style; state is re-hydrated from the state stream on next access.")]
public int MaxPartitionStateCacheSize { get; init; } = 100_000;
```

- [ ] **Step 2: Add field to `ProjectionSubsystemOptions` record and class**

Open `src/KurrentDB.Projections.Management/ProjectionsSubsystem.cs`.

Update the record:

```csharp
public record ProjectionSubsystemOptions(
	int ProjectionWorkerThreadCount,
	ProjectionType RunProjections,
	bool StartStandardProjections,
	TimeSpan ProjectionQueryExpiry,
	bool FaultOutOfOrderProjections,
	int CompilationTimeout,
	int ExecutionTimeout,
	int MaxProjectionStateSize,
	int MaxPartitionStateCacheSize);
```

Add the field to the class, alongside `_maxProjectionStateSize`:

```csharp
private readonly int _maxPartitionStateCacheSize;
```

In the constructor body (after `_maxProjectionStateSize = projectionSubsystemOptions.MaxProjectionStateSize;`):

```csharp
_maxPartitionStateCacheSize = projectionSubsystemOptions.MaxPartitionStateCacheSize;
```

In `ConfigureApplication`, update the `ProjectionsStandardComponents` construction to pass the new field (add a trailing argument — the exact position matches the next task's edit to `ProjectionsStandardComponents`):

```csharp
var projectionsStandardComponents = new ProjectionsStandardComponents(
	_projectionWorkerThreadCount,
	_runProjections,
	leaderOutputBus: _leaderOutputBus,
	leaderOutputQueue: _leaderOutputQueue,
	leaderInputBus: _leaderInputBus,
	leaderInputQueue: _leaderInputQueue,
	_faultOutOfOrderProjections,
	_compilationTimeout,
	_executionTimeout,
	_maxProjectionStateSize,
	_maxPartitionStateCacheSize,
	projectionTrackers);
```

- [ ] **Step 3: Extend `ProjectionsStandardComponents`**

Open `src/KurrentDB.Projections.Shared/ProjectionsStandardComponents.cs`. Replace the class body:

```csharp
public class ProjectionsStandardComponents {
	public ProjectionsStandardComponents(
		int projectionWorkerThreadCount,
		ProjectionType runProjections,
		ISubscriber leaderOutputBus,
		IPublisher leaderOutputQueue,
		ISubscriber leaderInputBus,
		IPublisher leaderInputQueue,
		bool faultOutOfOrderProjections, int projectionCompilationTimeout, int projectionExecutionTimeout,
		int maxProjectionStateSize,
		int maxPartitionStateCacheSize,
		ProjectionTrackers projectionTrackers) {
		ProjectionWorkerThreadCount = projectionWorkerThreadCount;
		RunProjections = runProjections;
		LeaderOutputBus = leaderOutputBus;
		LeaderOutputQueue = leaderOutputQueue;
		LeaderInputQueue = leaderInputQueue;
		LeaderInputBus = leaderInputBus;
		FaultOutOfOrderProjections = faultOutOfOrderProjections;
		ProjectionCompilationTimeout = projectionCompilationTimeout;
		ProjectionExecutionTimeout = projectionExecutionTimeout;
		MaxProjectionStateSize = maxProjectionStateSize;
		MaxPartitionStateCacheSize = maxPartitionStateCacheSize;
		ProjectionTrackers = projectionTrackers;
	}

	public int ProjectionWorkerThreadCount { get; }
	public ProjectionType RunProjections { get; }
	public ISubscriber LeaderOutputBus { get; }
	public IPublisher LeaderOutputQueue { get; }
	public IPublisher LeaderInputQueue { get; }
	public ISubscriber LeaderInputBus { get; }
	public bool FaultOutOfOrderProjections { get; }
	public int ProjectionCompilationTimeout { get; }
	public int ProjectionExecutionTimeout { get; }
	public int MaxProjectionStateSize { get; }
	public int MaxPartitionStateCacheSize { get; }
	public ProjectionTrackers ProjectionTrackers { get; }
}
```

- [ ] **Step 4: Plumb through `ProjectionCoreService`**

Open `src/KurrentDB.Projections.Management/Services/Processing/ProjectionCoreService.cs`. Inside the constructor, update the `ProcessingStrategySelector` construction:

```csharp
_processingStrategySelector = new ProcessingStrategySelector(
	_subscriptionDispatcher,
	configuration.MaxProjectionStateSize,
	configuration.MaxPartitionStateCacheSize);
```

- [ ] **Step 5: Plumb through `ProcessingStrategySelector`**

Open `src/KurrentDB.Projections.Management/Services/Processing/Strategies/ProcessingStrategySelector.cs`. Replace the class:

```csharp
public class ProcessingStrategySelector {
	private readonly ILogger _logger = Log.ForContext<ProcessingStrategySelector>();
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private readonly int _maxProjectionStateSize;
	private readonly int _maxPartitionStateCacheSize;

	public ProcessingStrategySelector(
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		int maxProjectionStateSize,
		int maxPartitionStateCacheSize) {
		_subscriptionDispatcher = subscriptionDispatcher;
		_maxProjectionStateSize = maxProjectionStateSize;
		_maxPartitionStateCacheSize = maxPartitionStateCacheSize;
	}

	public ProjectionProcessingStrategy CreateProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		ProjectionNamesBuilder namesBuilder,
		IQuerySources sourceDefinition,
		ProjectionConfig projectionConfig,
		string handlerType, string query, bool enableContentTypeValidation,
		int engineVersion,
		Func<IProjectionStateHandler> stateHandlerFactory,
		IPublisher mainQueue) {

		if (engineVersion == ProjectionConstants.EngineV2) {
			return new ProjectionProcessingStrategyV2(
				name, projectionVersion, projectionConfig, sourceDefinition, _logger, _maxProjectionStateSize,
				_maxPartitionStateCacheSize,
				stateHandlerFactory, mainQueue);
		}

		return projectionConfig.StopOnEof
			? (ProjectionProcessingStrategy)
			new QueryProcessingStrategy(
				name,
				projectionVersion,
				stateHandlerFactory.Invoke(),
				projectionConfig,
				sourceDefinition,
				_logger,
				_subscriptionDispatcher,
				enableContentTypeValidation,
				_maxProjectionStateSize)
			: new ContinuousProjectionProcessingStrategy(
				name,
				projectionVersion,
				stateHandlerFactory.Invoke(),
				projectionConfig,
				sourceDefinition,
				_logger,
				_subscriptionDispatcher,
				enableContentTypeValidation,
				_maxProjectionStateSize);
	}
}
```

- [ ] **Step 6: Plumb through `ProjectionProcessingStrategyV2`**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/ProjectionProcessingStrategyV2.cs`. Replace the class:

```csharp
public class ProjectionProcessingStrategyV2 : ProjectionProcessingStrategy {
	private readonly string _name;
	private readonly ProjectionConfig _projectionConfig;
	private readonly IQuerySources _sourceDefinition;
	private readonly Func<IProjectionStateHandler> _stateHandlerFactory;
	private readonly IPublisher _mainQueue;
	private readonly int _maxPartitionStateCacheSize;

	public ProjectionProcessingStrategyV2(
		string name,
		ProjectionVersion projectionVersion, // todo: unused, might represent an important gap
		ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition,
		ILogger logger,
		int maxProjectionStateSize, // todo: unused, might represent an important gap
		int maxPartitionStateCacheSize,
		Func<IProjectionStateHandler> stateHandlerFactory,
		IPublisher mainQueue) {

		_name = name;
		_projectionConfig = projectionConfig;
		_sourceDefinition = sourceDefinition;
		_stateHandlerFactory = stateHandlerFactory;
		_mainQueue = mainQueue;
		_maxPartitionStateCacheSize = maxPartitionStateCacheSize;
	}

	public override ICoreProjectionControl Create(
		Guid projectionCorrelationId,
		IPublisher inputQueue,
		Guid workerId,
		ClaimsPrincipal runAs,
		IPublisher publisher,
		IODispatcher ioDispatcher,
		ITimeProvider timeProvider) {
		return new CoreProjectionV2(
			projectionCorrelationId,
			_name,
			publisher,
			inputQueue,
			ioDispatcher,
			runAs,
			_sourceDefinition,
			_stateHandlerFactory,
			_projectionConfig,
			_mainQueue,
			_maxPartitionStateCacheSize);
	}
}
```

- [ ] **Step 7: Accept the value in `CoreProjectionV2` and plumb to engine config**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs`.

Add a field next to the other readonly fields:

```csharp
readonly int _maxPartitionStateCacheSize;
```

Extend the constructor:

```csharp
public CoreProjectionV2(
	Guid projectionCorrelationId,
	string projectionName,
	IPublisher publisher,
	IPublisher inputQueue,
	IODispatcher ioDispatcher,
	ClaimsPrincipal runAs,
	IQuerySources sourceDefinition,
	Func<IProjectionStateHandler> stateHandlerFactory,
	ProjectionConfig projectionConfig,
	IPublisher mainQueue,
	int maxPartitionStateCacheSize) {

	_projectionCorrelationId = projectionCorrelationId;
	_projectionName = projectionName;
	_publisher = publisher;
	_mainQueue = mainQueue;
	_ioDispatcher = ioDispatcher;
	_runAs = runAs;
	_sourceDefinition = sourceDefinition;
	_stateHandlerFactory = stateHandlerFactory;
	_projectionConfig = projectionConfig;
	_maxPartitionStateCacheSize = maxPartitionStateCacheSize;
	_checkpointStreamId = $"$projections-{projectionName}-checkpoint";
}
```

In `StartEngine`, replace the temporary literal with the field and remove the `// TEMP:` comment:

```csharp
var config = new ProjectionEngineV2Config {
	ProjectionName = _projectionName,
	SourceDefinition = _sourceDefinition,
	StateHandlerFactory = _stateHandlerFactory,
	MaxPartitionStateCacheSize = _maxPartitionStateCacheSize,
	CheckpointAfterMs = _projectionConfig.CheckpointAfterMs,
	CheckpointHandledThreshold = _projectionConfig.CheckpointHandledThreshold,
	CheckpointUnhandledBytesThreshold = _projectionConfig.CheckpointUnhandledBytesThreshold,
	EmitEnabled = _projectionConfig.EmitEventEnabled
};
```

- [ ] **Step 8: Pass option in `ClusterVNodeHostedService`**

Open `src/KurrentDB/ClusterVNodeHostedService.cs`. Update the `ProjectionSubsystemOptions` construction:

```csharp
? options.WithPlugableComponent(new ProjectionsSubsystem(
	new ProjectionSubsystemOptions(
		options.Projection.ProjectionThreads,
		projectionMode,
		startStandardProjections,
		TimeSpan.FromMinutes(options.Projection.ProjectionsQueryExpiry),
		options.Projection.FaultOutOfOrderProjections,
		options.Projection.ProjectionCompilationTimeout,
		options.Projection.ProjectionExecutionTimeout,
		options.Projection.MaxProjectionStateSize,
		options.Projection.MaxPartitionStateCacheSize)))
: options;
```

- [ ] **Step 9: Build**

Run: `dotnet build -c Release /p:Platform=ARM64 --framework=net10.0 --maxcpucount:1 src/KurrentDB.sln 2>&1 | tail -10`

Expected: `Build succeeded`. Any remaining test construction sites of `ProjectionCoreService`, `ProcessingStrategySelector`, `ProjectionProcessingStrategyV2`, `ProjectionsStandardComponents`, `ProjectionSubsystemOptions`, or `CoreProjectionV2` will fail to compile — grep and update each one.

Run: `grep -rn "new ProjectionsStandardComponents\|new ProjectionSubsystemOptions\|new ProcessingStrategySelector\|new ProjectionProcessingStrategyV2\|new CoreProjectionV2" src --include="*.cs" | grep -v bin | grep -v obj`

For each hit, add the matching argument (a literal like `100_000` is fine in test fixtures). Rebuild.

- [ ] **Step 10: Run the affected test suites**

Run:
```
dotnet test src/KurrentDB.Projections.V2.Tests/ -c Release /p:Platform=ARM64 --framework net10.0
dotnet test src/KurrentDB.Projections.Core.Tests/ -c Release /p:Platform=ARM64 --framework net10.0
```

Expected: all tests pass.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(projections-v2): wire MaxPartitionStateCacheSize through subsystem

Adds ClusterVNodeOptions.Projection.MaxPartitionStateCacheSize (default
100_000) and threads it through ProjectionSubsystemOptions →
ProjectionsStandardComponents → ProjectionCoreService →
ProcessingStrategySelector → ProjectionProcessingStrategyV2 →
CoreProjectionV2 → ProjectionEngineV2Config. Operators now have a
single knob to bound V2 partition-state memory.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Expose cache stats via `ProjectionStatistics`

**Files:**

- Modify: `src/KurrentDB.Projections.Shared/Services/ProjectionStatistics.cs`
- Modify: `src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs`

- [ ] **Step 1: Add fields to `ProjectionStatistics`**

Open `src/KurrentDB.Projections.Shared/Services/ProjectionStatistics.cs`. Append inside the class (after `StateSizeLimit`):

```csharp
public long PartitionStateCacheSize { get; set; }
public long PartitionStateCacheEvictions { get; set; }
```

- [ ] **Step 2: Populate them in `CoreProjectionV2.PublishStatistics`**

Open `src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs`. Replace `PublishStatistics`:

```csharp
void PublishStatistics(ProjectionEngineV2 engine) {
	var metrics = engine.GetCacheMetrics();
	var stats = new ProjectionStatistics {
		Name = _projectionName,
		EffectiveName = _projectionName,
		ProjectionId = 0,
		Status = engine.IsFaulted
			? "Faulted"
			: engine.IsStopped
				? "Stopped"
				: engine.IsStopping
					? "Stopping"
					: "Running",
		StateReason = "",
		BufferedEvents = 0,
		EventsProcessedAfterRestart = (int)engine.TotalEventsProcessed,
		PartitionStateCacheSize = metrics.Size,
		PartitionStateCacheEvictions = metrics.Evictions
	};

	_publisher.Publish(new CoreProjectionStatusMessage.StatisticsReport(
		_projectionCorrelationId, stats, _statisticsSequentialNumber++));
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build -c Release /p:Platform=ARM64 --framework=net10.0 --maxcpucount:1 src/KurrentDB.sln 2>&1 | tail -5`

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ -c Release /p:Platform=ARM64 --framework net10.0`

Expected: build succeeded, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/KurrentDB.Projections.Shared/Services/ProjectionStatistics.cs \
        src/KurrentDB.Projections.V2/Services/Processing/V2/CoreProjectionV2.cs
git commit -m "$(cat <<'EOF'
feat(projections-v2): report partition state cache stats

ProjectionStatistics gains PartitionStateCacheSize and
PartitionStateCacheEvictions, populated from ProjectionEngineV2's
shared cache. Gives operators a signal that caches are under pressure
before memory behaviour changes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Integration test — eviction + stream fallback

**Files:**

- Create: `src/KurrentDB.Projections.V2.Tests/Integration/PartitionStateCacheEvictionTests.cs`

**Intent:** exercise a `fromAll().partitionBy(stream)` projection with `MaxPartitionStateCacheSize = 4` and at least 10 distinct partition keys. After running, assert eviction counter > 0, shared-cache size ≤ 4, and `GetState` for an evicted partition returns the correct state (stream fallback).

- [ ] **Step 1: Read existing end-to-end test to match its conventions**

Run: `head -120 src/KurrentDB.Projections.V2.Tests/Integration/ProjectionEngineV2EndToEndTests.cs`

Note the fixture wiring (harness, projection start, event feed, state query). Reuse the same fixture helpers; do not duplicate.

- [ ] **Step 2: Write the failing test**

Create `src/KurrentDB.Projections.V2.Tests/Integration/PartitionStateCacheEvictionTests.cs`:

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

// Imports match ProjectionEngineV2EndToEndTests; replicate them when
// writing this test rather than deriving them from scratch.

namespace KurrentDB.Projections.V2.Tests.Integration;

public class PartitionStateCacheEvictionTests {
	// Replace the base fixture / harness references with whatever
	// ProjectionEngineV2EndToEndTests uses. The existing end-to-end
	// test already constructs a projection, feeds events, waits for
	// checkpoint, and queries state — model this test on that shape.

	[Test]
	public async Task evicted_partition_state_is_recovered_from_stream() {
		const int capacity = 4;
		const int distinctPartitions = 10;

		// 1. Build a projection: fromAll().partitionBy(e => e.EventStreamId).state({ count: 0 }).when(...).
		// 2. Construct the engine with MaxPartitionStateCacheSize = capacity.
		// 3. Feed one event to each of distinctPartitions distinct streams, then a second event to
		//    stream "p1" (a partition whose state is guaranteed evicted by the time the second event arrives,
		//    because distinctPartitions > capacity and SIEVE will have swept).
		// 4. Wait for the projection to write a checkpoint (so state is flushed to the -state stream).
		// 5. Kill and restart the engine — or query GetState for "p1".
		// 6. Assert GetState("p1") returns count == 2 (proving stream fallback worked) and
		//    eviction counter > 0.

		// Concrete fixture wiring: follow ProjectionEngineV2EndToEndTests.cs. Do not invent a new
		// harness. If a convenience helper is missing (e.g. "set cache capacity on the engine
		// config"), add the minimal parameter to that helper rather than duplicating its body.
	}

	[Test]
	public async Task eviction_counter_reflects_high_cardinality_load() {
		const int capacity = 4;
		const int distinctPartitions = 20;

		// 1. Projection with partitionBy(stream), cache capacity = 4.
		// 2. Feed one event per stream across 20 streams.
		// 3. Wait for checkpoint.
		// 4. Read engine.GetCacheMetrics() — assert Size <= capacity AND Evictions >= distinctPartitions - capacity.
	}
}
```

**Note on test style:** these tests need the existing integration harness. The bodies above are annotated — the engineer implementing them should copy the relevant helper usage from `ProjectionEngineV2EndToEndTests.cs` rather than invent new fixtures. If the harness does not already expose `MaxPartitionStateCacheSize` as a parameter on projection-start helpers, add it as an optional parameter on the helper (default `100_000`) in this task's commit.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ --filter "FullyQualifiedName~PartitionStateCacheEvictionTests" -c Release /p:Platform=ARM64 --framework net10.0`

Expected: failure — either because the test body isn't fleshed out or the helper doesn't yet accept a cache-capacity parameter.

- [ ] **Step 4: Flesh out the test bodies against the actual harness**

Follow `ProjectionEngineV2EndToEndTests.cs` conventions. Concretely:

- Call the same projection-start helper used by existing end-to-end tests; thread `MaxPartitionStateCacheSize = capacity` through to `ProjectionEngineV2Config`.
- Feed events using the same event-append helper; one event per partition key.
- Wait for checkpoint completion with the same `await` helper used by existing tests.
- Call the engine's `GetCacheMetrics()` and assert.
- For the `evicted_partition_state_is_recovered_from_stream` test, call the `GetState` pathway the existing tests use (`CoreProjectionManagementMessage.GetState`) and assert the returned state.

Do not introduce new fakes if existing helpers cover the behaviour.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ --filter "FullyQualifiedName~PartitionStateCacheEvictionTests" -c Release /p:Platform=ARM64 --framework net10.0`

Expected: both tests pass. Specifically:

- `evicted_partition_state_is_recovered_from_stream`: `GetState("p1")` returns the post-two-events state, proving the stream fallback in `CoreProjectionV2.Handle(GetState)` and/or `LoadPartitionState` recovers evicted entries.
- `eviction_counter_reflects_high_cardinality_load`: `GetCacheMetrics().Size <= 4` and `Evictions >= 16`.

- [ ] **Step 6: Run the full V2 test suite to check for regressions**

Run: `dotnet test src/KurrentDB.Projections.V2.Tests/ -c Release /p:Platform=ARM64 --framework net10.0`

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/KurrentDB.Projections.V2.Tests/Integration/PartitionStateCacheEvictionTests.cs \
        src/KurrentDB.Projections.V2.Tests/
git commit -m "$(cat <<'EOF'
test(projections-v2): integration coverage for partition cache eviction

Exercises the full eviction + stream-fallback path: high-cardinality
projection with a small cache cap, verifies Size <= capacity, eviction
counter climbs, and GetState for an evicted partition recovers the
correct state from the -state stream.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**Spec coverage:**

- ✅ Replace `PartitionProcessor._stateCache` and `ProjectionEngineV2._partitionStates` with bounded caches — Tasks 1, 3, 4.
- ✅ DotNext `RandomAccessCache` — Task 1.
- ✅ System-wide `MaxPartitionStateCacheSize` flowing through the subsystem chain — Tasks 2, 5.
- ✅ Silent eviction; correctness via existing `-state` stream fallback — Tasks 3 (`LoadPartitionState`) + 4 (unchanged `GetState` fallback) + 7 (verified).
- ✅ Eviction counter and current size in `ProjectionStatistics` — Task 6.
- ✅ Verbose log on eviction — Task 1 (`OnEviction`).
- ✅ Dispose order: run loop → partition drain → per-slot cache dispose → shared cache dispose — Tasks 3, 4.
- ✅ Engine-level integration test for ≥ 2× cap cardinality (4 capacity, 20 partitions) — Task 7.
- ✅ Config wiring test (compile-time enforced via `required` on engine config + runtime exercise via Task 7).

**Deviations from spec:** operator-visible cache size is reported from the shared cache only, not summed across per-slot caches. Rationale documented in Task 4 step 5: the shared cache is the faithful summary of partition-state memory pressure; per-slot caches are hot-path read caches whose sizes are bounded by the same cap. Sum-across-pools would be confusing (partition keys appear in multiple pools). If operators later want per-pool detail, the existing `GetCacheMetrics()` can be extended.

**Placeholder scan:** no TBDs, no "TODO" except the temporary `// TEMP:` marker in Task 2 Step 3 which is explicitly removed in Task 5 Step 7.

**Type consistency:** `MaxPartitionStateCacheSize` is consistently `int` throughout the chain. `PartitionStateCache` exposes `Count` and `Evictions` as `long`; `CacheMetrics` record mirrors the `long` types; `ProjectionStatistics.PartitionStateCacheSize` and `PartitionStateCacheEvictions` are `long` (matching — not `int`, to avoid truncation on long-running projections).

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-04-23-projections-v2-memory-control.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
