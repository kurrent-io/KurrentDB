# DuckDB Executor Integration (KurrentDB) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route all KurrentDB DuckDB work through the Kurrent.Quack `DuckDBExecutor` (owned worker/dispatcher threads) and expose `kurrentdb.duckdb.cpu.seconds` as an observable counter on `/metrics`, replacing the caller-side metric proposed in PR #5642 per the approved spec (`docs/superpowers/specs/2026-06-26-duckdb-cpu-attribution-design.md`, on the `spec/duckdb-cpu-attribution` branch / PR #5655).

**Architecture:** A new `DuckDBExecutorLifetime` owns database open (path + `memory_limit` + `threads`/`external_threads` via the executor) and replaces `DuckDBConnectionPoolLifetime`'s shared-RW-pool + per-Kestrel-connection READ_ONLY pool model entirely. Processors keep long-lived connections (obtained from the executor) with their `BufferedView` appenders, but every DuckDB *call* runs on a dispatcher thread — pinned-connection calls via a small Quack addendum (`ExecuteOn`), everything else via `Execute`. The metric is an OpenTelemetry `ObservableCounter` summing `executor.SampleCpu()` per role at scrape time.

**Tech Stack:** .NET 10, Kurrent.Quack `DuckDBExecutor` (feature branch `feat/duckdb-executor` in `/Users/tony/Documents/Kurrent.Quack`, not yet published — consumed via a local package feed until the prerelease exists), System.Diagnostics.Metrics + OTel Prometheus (existing pipeline).

## Global Constraints

- **Two repos:** Task 1 edits `/Users/tony/Documents/Kurrent.Quack` (branch `feat/duckdb-executor` — Tony owns push/PR; commits are fine, do NOT push). All other tasks edit `/Users/tony/Documents/KurrentDB` (branch `feat/duckdb-executor-integration`, off `origin/master` @ `b3c6d1c06`).
- **dotnet:** the PATH `dotnet` is SDK 8 and cannot build net10.0. Every dotnet command in BOTH repos: `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet ...`
- **Tests:** Quack: xunit.v3, `dotnet test test/Kurrent.Quack.Tests/Kurrent.Quack.Tests.csproj -- --filter-class "*Name*"`. KurrentDB: `dotnet test src/KurrentDB.SecondaryIndexing.Tests/KurrentDB.SecondaryIndexing.Tests.csproj` (xunit.v3/MTP, same `--filter-class` syntax after `--`).
- **Platform:** local machine is macOS — `ThreadCpuClock` reads 0 there by design; CPU *values* are CI-verified on Linux. All behavioral tests run fully on macOS.
- **KurrentDB conventions (CLAUDE.md):** no `= null` defaults on required params; no silent `?? fallback`; named booleans at call sites; tabs; file-scoped namespaces per project norms; per-event logs at Verbose, commits at Debug.
- **Executor API (final, from the Quack branch):** `DuckDBExecutor(string connectionString, int workerCount, int dispatcherCount)`; `ValueTask<T> Execute<T>(Func<DuckDBAdvancedConnection,T> op, CancellationToken ct)`; `IReadOnlyList<CpuSample> SampleCpu()` with `readonly record struct CpuSample(string Role, double CpuSeconds)` (roles `"worker"`/`"dispatcher"`); `ValueTask DisposeAsync()`; ctor throws `InvalidOperationException` if `current_setting('threads'/'external_threads')` didn't take effect; dispose-interrupt → `ObjectDisposedException`, caller-token interrupt → `OperationCanceledException`.
- **Gating:** Tasks 1–8 are executable NOW (no published package needed — Task 2 builds a local feed). Task 9 is GATED on the published Kurrent.Quack prerelease and flips the dependency; the KurrentDB PR cannot go green in CI before Task 9.
- Commit messages end with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

## Design decisions locked by exploration (origin/master @ b3c6d1c06)

- **No per-connection setup hook is needed.** Both `IDuckDBSetup` implementations (`IndexingDbSchema`, SchemaRegistry's `SchemaDbSchema`) are `OneTimeOnly = true`; the `_repeated` mechanism has zero implementations. One-time setups run once via `executor.Execute` at lifetime construction — same semantics as today (they run once on one shared-pool connection and their effects are visible database-wide).
- **`SchemaDbSchema` must keep running** through the new lifetime (SchemaRegistry registers it into the same `IEnumerable<IDuckDBSetup>`; its tables live in `kurrent.ddb`).
- **The per-Kestrel-connection READ_ONLY pool mechanism is deleted end-to-end**: `ConnectionScopedDuckDBConnectionPool`, the `ConnectionInterceptor` registration, `DuckDbConnectionPoolMiddleware` + `UseDuckDb()`, the `Pool` property on `ReadIndexEventsForward/Backward`, the `pool` params on `Enumerator.ReadIndexForwards/Backwards/IndexSubscription`, the `GetRequiredService<DuckDBConnectionPool>()` in `Streams.Read.cs:210`, `SecondaryIndexReaderBase.GetPool`, and MiniNode's manual feature middleware (`MiniNode.cs:273-277`). Keep Kestrel `UseConnectionInterceptors()` in ClusterVNodeApp — FlightSQL's own `ConnectionState` interceptor still needs it.
- **Streaming queries block a dispatcher by design** (spec §6): `QueryEngine.ExecuteAsync` runs its whole consume loop inside one `Execute` op, blocking that dispatcher on the consumer (`.AsTask().GetAwaiter().GetResult()`); `dispatcherCount` is the concurrency knob. Quack's `WorkItem` already registers `InterruptQueryOnCancellation` and maps interrupts, so QueryEngine sheds its own registration and interrupt mapping.
- **Thread-count defaults:** workers `Math.Clamp(Environment.ProcessorCount / 2, 2, 16)`, dispatchers `Math.Clamp(Environment.ProcessorCount / 2, 2, 8)`, overridable via configuration keys `KurrentDB:DuckDB:WorkerThreads` / `KurrentDB:DuckDB:DispatcherThreads` (initial defaults pending the spec §11 soak).
- **`TryIndex` row-appends stay on the subscription thread.** They write to the in-memory buffered chunk, not the task scheduler; their CPU is negligible and documented as excluded. Flush/commit/checkpoint/reads/queries — all actual DuckDB calls — run on dispatchers.
- **PR #5642 closes** (spec §10): everything on that branch was the caller-side metric; this plan re-creates the two keepable lines (meter name in `metricsconfig.json`, serviceName-aware registration) fresh.

---

### Task 1: Quack addendum — `OpenConnection` + pinned-connection `ExecuteOn` [Quack repo, NOW]

**Files:**
- Modify: `src/Kurrent.Quack/DuckDBDispatcherPool.cs`
- Modify: `src/Kurrent.Quack/DuckDBExecutor.cs`
- Test: `test/Kurrent.Quack.Tests/DuckDBExecutorTests.cs`

**Interfaces:**
- Consumes: existing `DuckDBDispatcherPool` internals (`_queue`, `_slotLocks`, `_active`, `_disposing`, `WorkItem<T>`), `DuckDBExecutor._pool`.
- Produces: `DuckDBAdvancedConnection DuckDBExecutor.OpenConnection()` (caller-owned, non-pooled, from the executor's pool — caller disposes); `ValueTask<T> DuckDBExecutor.ExecuteOn<T>(DuckDBAdvancedConnection connection, Func<DuckDBAdvancedConnection,T> op, CancellationToken ct)` and the same on `DuckDBDispatcherPool` — runs `op` on a dispatcher thread against the CALLER's connection (published to the interrupt slot; caller guarantees the connection is not used concurrently, which the processors do).

- [ ] **Step 1: Failing test** (append to `DuckDBExecutorTests`):

```csharp
	[Fact]
	public async Task ExecuteOnRunsOnDispatcherThreadWithTheProvidedConnection() {
		await using var executor = new DuckDBExecutor(CreateRandomConnectionString(), workerCount: 2, dispatcherCount: 2);
		using var pinned = executor.OpenConnection();

		var (threadName, same) = await executor.ExecuteOn(pinned, conn => (Thread.CurrentThread.Name, ReferenceEquals(conn, pinned)), CancellationToken.None);

		Assert.StartsWith("duckdb-dispatcher-", threadName);
		Assert.True(same); // the op ran against the caller's connection, not a rented one
	}

	[Fact]
	public async Task ExecuteOnAfterDisposeFailsWithObjectDisposed() {
		var executor = new DuckDBExecutor(CreateRandomConnectionString(), workerCount: 2, dispatcherCount: 1);
		var pinned = executor.OpenConnection();
		await executor.DisposeAsync();
		await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
			await executor.ExecuteOn(pinned, _ => 0, CancellationToken.None));
		pinned.Dispose();
	}
```

- [ ] **Step 2: Verify fail** — `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet test test/Kurrent.Quack.Tests/Kurrent.Quack.Tests.csproj -- --filter-class "*DuckDBExecutorTests*"` → compile error (`OpenConnection`/`ExecuteOn` missing).

- [ ] **Step 3: Implement.** In `DuckDBDispatcherPool`, generalize the work item to carry an optional pinned connection and split `RunWithConnection`:

```csharp
	public ValueTask<T> Execute<T>(Func<DuckDBAdvancedConnection, T> op, CancellationToken ct)
		=> Submit(new WorkItem<T>(pinned: null, op, ct));

	/// <summary>Runs the operation on a dispatcher thread against the caller-provided connection. The caller
	/// must guarantee the connection is not used concurrently and owns its lifetime.</summary>
	public ValueTask<T> ExecuteOn<T>(DuckDBAdvancedConnection connection, Func<DuckDBAdvancedConnection, T> op, CancellationToken ct) {
		ArgumentNullException.ThrowIfNull(connection);
		return Submit(new WorkItem<T>(connection, op, ct));
	}

	private ValueTask<T> Submit<T>(WorkItem<T> item) {
		if (_disposing || !_queue.Writer.TryWrite(item))
			item.Fail(new ObjectDisposedException(nameof(DuckDBDispatcherPool)));
		return new ValueTask<T>(item, item.Version);
	}

	// RunWithConnection keeps its exact body but gains a sibling that skips the rent:
	private void RunWithPinnedConnection(int index, DuckDBAdvancedConnection connection, Action<DuckDBAdvancedConnection> body) {
		lock (_slotLocks[index]) {
			if (_disposing)
				throw new ObjectDisposedException(nameof(DuckDBDispatcherPool)); // fail before any user code runs
			Volatile.Write(ref _active[index], connection);
		}
		try {
			body(connection);
		} finally {
			lock (_slotLocks[index]) Volatile.Write(ref _active[index], null);
		}
	}
```

`WorkItem<T>` gains the `DuckDBAdvancedConnection? pinned` primary-ctor parameter; its `Run` picks the path (everything else — the catch filters, `InterruptQueryOnCancellation`, exactly-once completion — is unchanged):

```csharp
		public void Run(DuckDBDispatcherPool owner, int index) {
			try {
				ct.ThrowIfCancellationRequested();
				if (pinned is null)
					owner.RunWithConnection(index, Body);
				else
					owner.RunWithPinnedConnection(index, pinned, Body);
			} catch (DuckDBException e) when (e.ErrorType == DuckDBErrorType.Interrupt && ct.IsCancellationRequested) {
				_core.SetException(new OperationCanceledException(ct));
			} catch (DuckDBException e) when (e.ErrorType == DuckDBErrorType.Interrupt && owner._disposing) {
				_core.SetException(new ObjectDisposedException(nameof(DuckDBDispatcherPool)));
			} catch (Exception e) {
				_core.SetException(e);
			}

			void Body(DuckDBAdvancedConnection connection) {
				using var _ = connection.InterruptQueryOnCancellation(ct);
				_core.SetResult(op(connection));
			}
		}
```

In `DuckDBExecutor`:

```csharp
	/// <summary>Opens a caller-owned, non-pooled connection to the executor's database. The caller disposes it;
	/// run all DuckDB calls on it via <see cref="ExecuteOn{T}"/> so they execute on measured dispatcher threads.</summary>
	public DuckDBAdvancedConnection OpenConnection() => _pool.Open();

	public ValueTask<T> ExecuteOn<T>(DuckDBAdvancedConnection connection, Func<DuckDBAdvancedConnection, T> op, CancellationToken ct)
		=> _dispatchers.ExecuteOn(connection, op, ct);
```

- [ ] **Step 4: Verify pass** (focused), then full Quack suite: expect **78 = 76 passed + 2 skipped** on macOS.
- [ ] **Step 5: Commit** on `feat/duckdb-executor`: `feat: OpenConnection + pinned-connection ExecuteOn (KurrentDB integration addendum)` — do NOT push (Tony owns the remote).

---

### Task 2: Local package feed [Quack repo, NOW — build step, no commit]

- [ ] **Step 1:** `cd /Users/tony/Documents/Kurrent.Quack && DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet pack Kurrent.Quack.slnx -c Release -p:MinVerVersionOverride=0.0.0-local.1 -o .local-packages`
- [ ] **Step 2:** Verify: `ls .local-packages` → `Kurrent.Quack.0.0.0-local.1.nupkg` and `Kurrent.Quack.Arrow.0.0.0-local.1.nupkg` (both must exist — KurrentDB references both and `ConnectionHelpers` moved from Arrow to core, so they must move in lockstep).
- [ ] **Step 3:** No commit. Re-run this task's Step 1 with `-local.2` etc. whenever Task 1's code changes.

---

### Task 3: KurrentDB consumes the local packages [NOW]

**Files:**
- Create: `NuGet.config` (repo root, `/Users/tony/Documents/KurrentDB/NuGet.config`) — **not committed**: add the literal line `NuGet.config` to `.git/info/exclude`.
- Modify: `src/Directory.Packages.props:71-72` (Kurrent.Quack + Kurrent.Quack.Arrow versions).

- [ ] **Step 1:** Write `NuGet.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<add key="quack-local" value="/Users/tony/Documents/Kurrent.Quack/.local-packages" />
	</packageSources>
</configuration>
```

(No `<clear/>` — nuget.org stays via defaults.) Then `echo "NuGet.config" >> .git/info/exclude`.

- [ ] **Step 2:** In `src/Directory.Packages.props` change both lines to `Version="0.0.0-local.1"`:

```xml
    <PackageVersion Include="Kurrent.Quack" Version="0.0.0-local.1" />
    <PackageVersion Include="Kurrent.Quack.Arrow" Version="0.0.0-local.1" />
```

- [ ] **Step 3:** Restore + build the affected projects: `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet build src/KurrentDB.SecondaryIndexing/KurrentDB.SecondaryIndexing.csproj` → succeeds (nothing uses the new API yet; this proves the feed works).
- [ ] **Step 4:** Commit ONLY `src/Directory.Packages.props`: `chore: consume Kurrent.Quack executor build (local feed placeholder version)` — the commit message must note the version is a placeholder flipped in the final task.

---

### Task 4: `DuckDBExecutorLifetime` replaces `DuckDBConnectionPoolLifetime` [NOW]

**Files:**
- Create: `src/KurrentDB.Core/DuckDB/DuckDBExecutorLifetime.cs`
- Modify: `src/KurrentDB.Core/DuckDB/InjectionExtensions.cs` (rewrite `AddDuckDb`; delete the interceptor/middleware wiring and `UseDuckDb`)
- Delete: `src/KurrentDB.Core/DuckDB/DuckDBConnectionPoolLifetime.cs`, `src/KurrentDB.Core/DuckDB/DuckDbConnectionPoolMiddleware.cs` (contains `ConnectionScopedDuckDBConnectionPool`)
- Modify: `src/KurrentDB.Core/ClusterVNodeStartup.cs:141` (remove `app.UseDuckDb();`) and `:317` (new `AddDuckDb` args)
- Modify: `src/KurrentDB.Core.Testing/Helpers/MiniNode.cs:273-277` (delete the manual `ConnectionScopedDuckDBConnectionPool` middleware block; keep `Node.Startup.ConfigureServices/Configure` calls)

**Interfaces:**
- Consumes: `DuckDBExecutor` (Quack), `IDuckDBSetup` (existing), `TFChunkDbConfig` (`InMemDb`, `Path`).
- Produces: `sealed class DuckDBExecutorLifetime : Disposable, IHostedService` with `DuckDBExecutor Executor { get; }`; DI: `AddDuckDb(this IServiceCollection services, string serviceName, int workerCount, int dispatcherCount)` registering the lifetime (hosted) + `DuckDBExecutor` singleton (`sp => sp.GetRequiredService<DuckDBExecutorLifetime>().Executor`). **`DuckDBConnectionPool` is no longer registered** — remaining consumers migrate in Tasks 5–7 (the solution will not build again until Task 7 completes; run per-project builds as directed per task).

- [ ] **Step 1: Implementation** (this task is infrastructure — its test is Task 8's fixture suite plus the per-project build gates; write the code first):

```csharp
// src/KurrentDB.Core/DuckDB/DuckDBExecutorLifetime.cs
// (KurrentDB license header)
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Core.DuckDB;

// Owns the DuckDB executor: database open (with thread + memory settings applied at open),
// one-time schema setups, the shutdown checkpoint, and executor disposal.
public sealed class DuckDBExecutorLifetime : Disposable, IHostedService {
	private readonly ILogger<DuckDBExecutorLifetime> _log;
	[CanBeNull] private string _tempPath;

	public DuckDBExecutor Executor { get; }

	public DuckDBExecutorLifetime(
		TFChunkDbConfig config,
		IEnumerable<IDuckDBSetup> setups,
		int workerCount,
		int dispatcherCount,
		[CanBeNull] ILogger<DuckDBExecutorLifetime> log) {
		_log = log ?? NullLogger<DuckDBExecutorLifetime>.Instance;

		var path = config.InMemDb ? GetTempPath() : $"{config.Path}/kurrent.ddb";
		var memoryMib = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 * 0.25); // 25% of RAM, as before
		var connectionString = $"Data Source={path};memory_limit={memoryMib}MB";

		Executor = new DuckDBExecutor(connectionString, workerCount, dispatcherCount);
		_log.LogInformation("DuckDB executor started at {path}: {workers} workers, {dispatchers} dispatchers, memory_limit {memory}MB",
			path, workerCount, dispatcherCount, memoryMib);

		// One-time setups (IndexingDbSchema, SchemaDbSchema) — effects are database-wide, so running
		// them once on any executor-owned connection preserves today's semantics exactly.
		Executor.Execute(connection => {
			foreach (var setup in setups)
				setup.Execute(connection);
			return 0;
		}, CancellationToken.None).AsTask().GetAwaiter().GetResult();

		return;

		string GetTempPath() {
			_tempPath = Path.GetTempFileName();
			File.Delete(_tempPath); // DuckDB refuses a pre-existing empty file; recreate at the same path
			return _tempPath;
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken) {
		_log.LogDebug("Checkpointing DuckDB");
		await Executor.Execute(connection => {
			connection.Checkpoint();
			return 0;
		}, cancellationToken);
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
			if (_tempPath != null) {
				try {
					File.Delete(_tempPath);
				} catch (IOException) {
					// let the OS clean it up
				}
			}
		}
		base.Dispose(disposing);
	}
}
```

`InjectionExtensions.cs` becomes (whole file; deletes the interceptor, middleware, `UseDuckDb`, and the file-class provider):

```csharp
// (license header)
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Kurrent.Quack;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.DuckDB;

public static class InjectionExtensions {
	public static IServiceCollection AddDuckDb(this IServiceCollection services, string serviceName, int workerCount, int dispatcherCount) {
		services.AddSingleton(sp => new DuckDBExecutorLifetime(
			sp.GetRequiredService<TFChunkDbConfig>(),
			sp.GetServices<IDuckDBSetup>(),
			workerCount,
			dispatcherCount,
			sp.GetService<ILogger<DuckDBExecutorLifetime>>()));
		services.AddHostedService(sp => sp.GetRequiredService<DuckDBExecutorLifetime>());
		services.AddSingleton<DuckDBExecutor>(sp => sp.GetRequiredService<DuckDBExecutorLifetime>().Executor);
		services.AddSingleton(new DuckDBCpuMetrics(new Meter(DuckDBCpuMetrics.MeterName, "1.0.0"), serviceName)); // Task 5b wires the instrument
		return services;
	}
}
```

(Task 5b defines `DuckDBCpuMetrics`; to keep this task compiling on its own, add the class in THIS task as an empty shell `public class DuckDBCpuMetrics { public const string MeterName = "KurrentDB.DuckDB"; public DuckDBCpuMetrics(Meter meter, string serviceName) { } }` — Task 5b fills it. This is the one intentionally-thin seam between the two tasks.)

`ClusterVNodeStartup.cs`: delete line 141 (`app.UseDuckDb();` and its comment); replace line 317 with:

```csharp
		services.AddDuckDb(
			_metricsConfiguration.ServiceName,
			workerCount: _configuration.GetValue("KurrentDB:DuckDB:WorkerThreads", Math.Clamp(Environment.ProcessorCount / 2, 2, 16)),
			dispatcherCount: _configuration.GetValue("KurrentDB:DuckDB:DispatcherThreads", Math.Clamp(Environment.ProcessorCount / 2, 2, 8)));
```

`MiniNode.cs`: delete the `_webHost.Use(async (ctx, next) => { ... ConnectionScopedDuckDBConnectionPool ... });` block (lines 273-277) entirely; `Node.Startup.Configure(_webHost)` stays.

- [ ] **Step 2: Build gate** — `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet build src/KurrentDB.Core/KurrentDB.Core.csproj` → **expected to FAIL** only at `Streams.Read.cs:210` (`GetRequiredService<DuckDBConnectionPool>`) and `Enumerator.*` pool params — those are Task 6's files. If failures appear anywhere else, fix them here. Do NOT commit yet if Core doesn't build: Tasks 4–6 form one atomic commit train; commit at the end of this task only if Core builds after Step 3.
- [ ] **Step 3: To keep Task 4 independently committable**, apply the *mechanical deletion halves* of Task 6 that live in Core in this same commit: remove the `pool` parameter and `Pool` message property plumbing (`Enumerator.ReadIndex.cs:32,43-44,59`, `Enumerator.IndexSubscription.cs:32,50,~380`, `ClientMessage.IndexReads.cs:45,59,97,111`, `Streams.Read.cs:201-219` pool resolution and the three `pool:` arguments). Core now builds: verify with the Step-2 command → 0 errors.
- [ ] **Step 4: Commit** — `feat: DuckDB executor lifetime owns DB open; remove per-connection pool plumbing`

---

### Task 5: Migrate SecondaryIndexing processors to pinned connections [NOW]

**Files:**
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/ISecondaryIndexProcessor.cs` (`Commit()` → `ValueTask CommitAsync(CancellationToken)`)
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/Default/DefaultIndexProcessor.cs`
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/User/UserIndexProcessor.cs`
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/User/UserIndexEngine.cs`, `UserIndexEngineSubscription.cs` (pass `DuckDBExecutor`; `db.Rent` sites → `Execute`)
- Modify: `src/KurrentDB.SecondaryIndexing/Subscriptions/DefaultIndexSubscription.cs:96-101` and `UserIndexSubscription.cs` (await `CommitAsync`)
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/Default/DefaultIndexBuilder.cs` (dispose path)

**Interfaces:**
- Consumes: `DuckDBExecutor.OpenConnection()`, `ExecuteOn`, `Execute` (Task 1), DI `DuckDBExecutor` (Task 4).
- Produces: processors constructed with `DuckDBExecutor executor` instead of `DuckDBConnectionPool db`; `ValueTask CommitAsync(CancellationToken ct)` on `ISecondaryIndexProcessor` (and `UserIndexProcessor.CheckpointAsync`); `CaptureSnapshot` signatures unchanged (still take the caller's connection — readers own that, Task 6).

The transformation, shown complete on `DefaultIndexProcessor` (apply the identical pattern to `UserIndexProcessor` — its extra members are listed after):

- [ ] **Step 1:** Constructor: replace `DuckDBConnectionPool db` with `DuckDBExecutor executor`; store `private readonly DuckDBExecutor _executor;`. Replace `_connection = db.Open();` with `_connection = executor.OpenConnection();`. Replace the ctor-time read (`ReadLastIndexedRecord()` uses `_connection`) with a dispatcher-run call:

```csharp
		var (lastPosition, lastTimestamp) = executor
			.ExecuteOn(_connection, ReadLastIndexedRecord, CancellationToken.None)
			.AsTask().GetAwaiter().GetResult(); // ctor is synchronous; runs once at startup
```

and change `private (TFPos, DateTimeOffset) ReadLastIndexedRecord()` to take the connection: `private static (TFPos, DateTimeOffset) ReadLastIndexedRecord(DuckDBAdvancedConnection connection)` (same body, `connection.` instead of `_connection.`).

- [ ] **Step 2:** Commit → async, flush on a dispatcher against the pinned connection:

```csharp
	public async ValueTask CommitAsync(CancellationToken ct) {
		if (IsDisposingOrDisposed || !Interlocked.FalseToTrue(ref _committing))
			return;

		try {
			using var duration = Tracker.StartCommitDuration();
			await _executor.ExecuteOn(_connection, c => {
				_appender.Flush(); // appender is bound to _connection; runs on a measured dispatcher thread
				return 0;
			}, ct);
		} catch (Exception e) {
			_log.LogError(e, "Failed to commit records to index at log position {LogPosition}", LastIndexedPosition);
			throw;
		} finally {
			Volatile.Write(ref _committing, false);
		}
	}
```

`Dispose(bool)` calls the blocking form once — `CommitAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();` — before unregister/dispose (dispose stays synchronous, as today).

- [ ] **Step 3:** `UserIndexProcessor` additionally: `Checkpoint(position, timestamp)` → `ValueTask CheckpointAsync(TFPos position, DateTime timestamp, CancellationToken ct)` wrapping `UserIndexSql.SetCheckpoint(_connection, args)` in `_executor.ExecuteOn(_connection, ...)`; ctor `GetLastKnownRecord()` gets the same ExecuteOn treatment as Step 1; `_sql.CreateUserIndex(_connection)` in the ctor also runs via `ExecuteOn` (it's a DuckDB DDL call).
- [ ] **Step 4:** `UserIndexEngine`/`UserIndexEngineSubscription`: replace the `DuckDBConnectionPool db` parameters with `DuckDBExecutor executor` end-to-end; the two `db.Rent(out var connection)` sites (lines 109, 268) become `await executor.Execute(connection => { DeleteUserIndexTable(connection, name); return 0; }, ct)` (the catch-up cleanup loops over deleted indexes inside one Execute op). `StartUserIndex<TField>` passes `executor` into the processor and reader ctors.
- [ ] **Step 5:** Subscriptions: `DefaultIndexSubscription.ProcessEvents` batch commit becomes `await indexProcessor.CommitAsync(token);`; same in `UserIndexSubscription` (both its batch commit and its checkpoint call → `await ...CheckpointAsync(...)`).
- [ ] **Step 6: Build gate** — `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet build src/KurrentDB.SecondaryIndexing/KurrentDB.SecondaryIndexing.csproj` → FAILS only in readers/QueryEngine/StatsService (Task 6/7 files) if at all; processors, engine, subscriptions compile. (If the project can't build partially, proceed to Task 6 and commit Tasks 5+6 together — note it in the commit message.)
- [ ] **Step 7: Commit** — `feat: index processors run DuckDB work on executor dispatchers (pinned connections)`

---

### Task 6: Migrate readers [NOW]

**Files:**
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/SecondaryIndexReaderBase.cs`
- Modify: `src/KurrentDB.SecondaryIndexing/Indexes/Default/DefaultIndexReader.cs`, `Category/CategoryIndexReader.cs`, `EventType/EventTypeIndexReader.cs`, `User/UserIndexReader.cs`
- Modify: `src/KurrentDB.SecondaryIndexing/SecondaryIndexingPlugin.cs` (DI: readers/QueryEngine get `DuckDBExecutor`; drop pool registrations)

**Interfaces:**
- Consumes: `DuckDBExecutor.Execute` (rented connection per read op).
- Produces: `SecondaryIndexReaderBase(DuckDBExecutor executor, IReadIndex<string> index)`; abstract `GetDbRecordsForwards/Backwards` signatures change from `(DuckDBConnectionPool db, string? id, long startPosition, int maxCount, bool excludeFirst)` to `(DuckDBAdvancedConnection connection, string? id, long startPosition, int maxCount, bool excludeFirst)` — the base does the executor call; subclasses receive an open connection and keep their `CaptureSnapshot(connection)` + query bodies minus the `Rent` wrapper.

- [ ] **Step 1:** Base class: replace `GetPool`/`msg.Pool` usage (already deleted from the messages in Task 4 Step 3) with:

```csharp
		async ValueTask<(long, IReadOnlyList<ResolvedEvent>)> GetEventsForwards(long startPosition) {
			var indexPrepares = await executor.Execute(
				connection => GetDbRecordsForwards(connection, id, startPosition, msg.MaxCount, msg.ExcludeStart),
				token);
			var events = await reader.ReadRecords(indexPrepares, true, token);
			return (indexPrepares.Count, events);
		}
```

(same shape for backwards). Delete `GetPool` entirely.

- [ ] **Step 2:** Each reader override drops its `using (db.Rent(out var connection))` line and keeps the body:

```csharp
	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBAdvancedConnection connection, string? id, long startPosition, int maxCount, bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (processor.CaptureSnapshot(connection)) {
			// existing ExecuteQuery<...>(...).CopyTo(records) body unchanged
		}
		return records;
	}
```

- [ ] **Step 3:** Plugin DI (`SecondaryIndexingPlugin.ConfigureServices`): reader/`QueryEngine`/processor registrations resolve `DuckDBExecutor` (from Core DI) instead of `DuckDBConnectionPool`.
- [ ] **Step 4: Build gate** — SecondaryIndexing project builds except QueryEngine/StatsService (Task 7). Commit (or fold with Task 5 per its Step 6 note) — `feat: index readers execute on DuckDB dispatchers`

---

### Task 7: Migrate QueryEngine, Rewriter, StatsService [NOW]

**Files:**
- Modify: `src/KurrentDB.SecondaryIndexing/Query/QueryEngine.cs`, `QueryEngine.Rewriter.cs`
- Modify: `src/KurrentDB.SecondaryIndexing/Stats/StatsService.cs`

**Interfaces:**
- Consumes: `DuckDBExecutor.Execute`; Quack's built-in cancellation-interrupt + interrupt→OCE mapping (so QueryEngine's own `InterruptQueryOnCancellation` and `DuckDBException(Interrupt)` catch are DELETED — Quack raises `OperationCanceledException` for token-interrupts already).
- Produces: `QueryEngine(DefaultIndexProcessor defaultIndex, UserIndexEngine userIndex, DuckDBExecutor executor)`.

- [ ] **Step 1:** `ExecuteAsync` — the whole rent/snapshot/prepare/consume/cleanup pipeline moves inside ONE `Execute` op (spec §6: streaming blocks a dispatcher by design; `dispatcherCount` bounds concurrent streams):

```csharp
	public async ValueTask ExecuteAsync<TConsumer>(ReadOnlyMemory<byte> preparedQuery,
		TConsumer consumer, QueryExecutionOptions options, CancellationToken token)
		where TConsumer : IQueryResultConsumer {
		var parsedQuery = new PreparedQuery(preparedQuery.Span);
		if (options.CheckIntegrity)
			CheckIntegrity(in parsedQuery);

		await executor.Execute(connection => {
			var snapshots = new PoolingBufferWriter<SnapshotInfo> { Capacity = parsedQuery.ViewCount + 1 };
			var statement = default(PreparedStatement);
			var reader = default(QueryResultReader);
			try {
				CaptureSnapshots(in parsedQuery, connection, snapshots, token);
				statement = new(connection, parsedQuery.Query);
				consumer.Bind(new QueryBinder(in statement));
				reader = new(in statement, consumer.UseStreaming);
				consumer.ConsumeAsync(reader, token).AsTask().GetAwaiter().GetResult(); // dispatcher blocks: by design
				reader.ThrowOnError();
				return 0;
			} finally {
				reader?.Dispose();
				statement.Dispose();
				Disposable.Dispose(snapshots.WrittenMemory.Span);
				snapshots.Dispose();
			}
		}, token);
	}
```

(`InterruptQueryOnCancellation` and the `catch (DuckDBException … Interrupt)` are gone — Quack owns both.) `GetArrowSchema` gets the same wrap (no consume loop). `QueryEngine.Rewriter.cs`'s two `sharedPool.Rent` sites become `executor.Execute(connection => …)` — `PrepareQuery` becomes `ValueTask<MemoryOwner<byte>> PrepareQueryAsync(...)`; update its callers (`FlightSqlServer.PlainQuery/PreparedStmt`, `QueryService` in `src/KurrentDB/Components/Query/QueryService.cs`, and the `ReadTests` integration test) to await it.

- [ ] **Step 2:** `StatsService` — each method's `using var connection = _pool.Open(); using var snapshot = _defaultIndex.CaptureSnapshot(connection); …query…` becomes:

```csharp
		return await _executor.Execute(connection => {
			using var snapshot = _defaultIndex.CaptureSnapshot(connection);
			// existing query body unchanged
		}, ct);
```

(methods become async `ValueTask<…>`; update their callers in the UI stats components — `src/KurrentDB/Components/Stats/*.razor.cs` / `UiStatsService` — mechanically to await.)

- [ ] **Step 3: Build gate** — full solution now builds: `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet build src/KurrentDB.sln` (or the host project `src/KurrentDB/KurrentDB.csproj` if the sln is too slow) → 0 errors.
- [ ] **Step 4: Commit** — `feat: QueryEngine and stats run on DuckDB dispatchers; Quack owns query cancellation`

---

### Task 5b (fold into whichever of Tasks 4–7 lands last before tests): the metric [NOW]

**Files:**
- Modify: `src/KurrentDB.Core/DuckDB/DuckDBCpuMetrics.cs` (the Task-4 shell becomes real)
- Modify: `src/KurrentDB/metricsconfig.json:5-13` (add `"KurrentDB.DuckDB"` to `Meters`)
- Test: `src/KurrentDB.SecondaryIndexing.Tests/Diagnostics/DuckDBCpuMetricsTests.cs` (new)

- [ ] **Step 1: Failing test:**

```csharp
// namespace KurrentDB.SecondaryIndexing.Tests.Diagnostics; xunit.v3
public class DuckDBCpuMetricsTests {
	[Fact]
	public void observable_counter_reports_per_role_cpu_sums() {
		using var meter = new Meter("test");
		var samples = new List<DuckDBExecutor.CpuSample> {
			new("worker", 1.5), new("worker", 0.5), new("dispatcher", 0.25),
		};
		_ = new DuckDBCpuMetrics(meter, "kurrentdb", () => samples);

		List<(double Value, string Role)> observed = [];
		using var listener = new MeterListener();
		listener.InstrumentPublished = (i, l) => {
			if (i.Meter == meter && i.Name == "kurrentdb.duckdb.cpu.seconds") l.EnableMeasurementEvents(i);
		};
		listener.SetMeasurementEventCallback<double>((_, value, tags, _) => {
			foreach (var t in tags) if (t.Key == "role") observed.Add((value, (string)t.Value!));
		});
		listener.Start();
		listener.RecordObservableInstruments();

		Assert.Contains(observed, m => m is { Role: "worker", Value: 2.0 });
		Assert.Contains(observed, m => m is { Role: "dispatcher", Value: 0.25 });
	}
}
```

- [ ] **Step 2:** Implementation (`DuckDBCpuMetrics`): ctor `(Meter meter, string serviceName, Func<IReadOnlyList<DuckDBExecutor.CpuSample>> sampleCpu)`:

```csharp
	meter.CreateObservableCounter($"{serviceName}.duckdb.cpu.seconds", Observe,
		description: "CPU time consumed by DuckDB's executor threads, in seconds");

	IEnumerable<Measurement<double>> Observe() {
		double workers = 0, dispatchers = 0;
		foreach (var s in sampleCpu())
			if (s.Role == "worker") workers += s.CpuSeconds; else dispatchers += s.CpuSeconds;
		yield return new(workers, new KeyValuePair<string, object?>("role", "worker"));
		yield return new(dispatchers, new KeyValuePair<string, object?>("role", "dispatcher"));
	}
```

`AddDuckDb` wires it: `services.AddSingleton(sp => new DuckDBCpuMetrics(new Meter(DuckDBCpuMetrics.MeterName, "1.0.0"), serviceName, () => sp.GetRequiredService<DuckDBExecutorLifetime>().Executor.SampleCpu()));` plus `services.AddSingleton<IHostedService>(...)`? No — the metrics object must be *instantiated*; resolve it eagerly from the lifetime's hosted `StartAsync` or register it as an activated singleton: `services.AddActivatedSingleton<DuckDBCpuMetrics>(...)` (net8+ API) so the instrument exists without a consumer. metricsconfig.json gains `"KurrentDB.DuckDB",` after `"KurrentDB.SecondaryIndexes",`.

- [ ] **Step 3:** Verify test passes; commit — `feat: kurrentdb.duckdb.cpu.seconds observable counter over executor CPU samples`

---

### Task 8: Test infrastructure + full verification [NOW]

**Files:**
- Modify: `src/KurrentDB.SecondaryIndexing.Tests/Fixtures/DuckDbIntegrationTest.cs` (construct a `DuckDBExecutor` instead of a raw pool; processors/readers built from it)
- Modify: any fixture/test constructing processors/readers directly (`Indexes/DefaultIndexProcessorTests.cs`, `Indexes/DefaultIndexReaderTests/IndexTestBase.cs`, `IntegrationTests/ReadTests.cs` for `PrepareQueryAsync`)
- No changes needed to `SecondaryIndexingFixture`/`ClusterVNodeApp` (they boot the real node → new DI path exercises everything).

- [ ] **Step 1:** `DuckDbIntegrationTest` swaps `new DuckDBConnectionPool(...)` for `new DuckDBExecutor($"Data Source={dbPath};", workerCount: 2, dispatcherCount: 2)` and runs the schema setup via `Executor.Execute(...)`; expose `protected readonly DuckDBExecutor Executor;` (tests that rented connections directly now go through `Executor.Execute`).
- [ ] **Step 2:** Run the full SecondaryIndexing suite: `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet test src/KurrentDB.SecondaryIndexing.Tests/KurrentDB.SecondaryIndexing.Tests.csproj` → all green (count will match master's suite plus the new metrics test).
- [ ] **Step 3:** Live smoke (recipe from dev-environment memory): build the host, run `--insecure --node-port 2119 --replication-port 1119 --enable-atom-pub-over-http` with `KURRENTDB__SECONDARYINDEXING__OPTIONS__COMMITBATCHSIZE=1`, write one event via AtomPub, then `curl -s http://localhost:2119/metrics | grep duckdb_cpu` → expect `kurrentdb_duckdb_cpu_seconds_total{...role="worker"...}` and `role="dispatcher"` series with **non-zero dispatcher values** (macOS reads 0 — so on macOS assert only presence of the series; values are CI/Linux). Also verify node startup log shows the executor line and clean shutdown checkpoints.
- [ ] **Step 4:** Commit — `test: fixtures build on DuckDB executor; live /metrics smoke verified`

---

### Task 9: Flip to the published package + PR [GATED on Tony's Quack prerelease]

- [ ] **Step 1:** Delete `NuGet.config` (and its `.git/info/exclude` line); set `src/Directory.Packages.props` Kurrent.Quack + Kurrent.Quack.Arrow to the real prerelease version (from Tony's tag); restore + full build + full SecondaryIndexing suite.
- [ ] **Step 2:** Commit — `chore: consume published Kurrent.Quack <version>`; push branch; open PR against master titled "Route DuckDB through the owned-thread executor; add kurrentdb.duckdb.cpu.seconds" with the spec + Quack PR linked; verify CI (the SecondaryIndexing suite on Linux exercises real CPU values end-to-end).
- [ ] **Step 3:** Close PR #5642 with a comment: superseded by this PR per the approved spec (§10) — the caller-side measurement is not being merged; the meter name + config landed here instead.

## Self-review notes

- Spec coverage: §3–§7 (executor ownership, call-site migration incl. streaming-inside-one-op, metric) → Tasks 4–7, 5b; §8 lifecycle (shutdown checkpoint via executor, dispose order) → Task 4; §10 disposition of #5642 → Task 9; §11 dispatcher sizing → config knobs + defaults in Task 4 (soak still owed before GA — out of plan scope, tracked in memory).
- The `CommitAsync`/`CheckpointAsync`/`PrepareQueryAsync`/`StatsService` async ripples are enumerated with their caller lists; implementers must chase compiler errors within the named files only — any ripple beyond them is a finding to report, not silently fix.
- Known intentionally-accepted losses: READ_ONLY access-mode isolation for reads (all connections now RW; reads are SELECTs); per-Kestrel-connection pool isolation (dispatcherCount now bounds read concurrency); `TryIndex` buffered row-append CPU (unmeasured, negligible, documented).
