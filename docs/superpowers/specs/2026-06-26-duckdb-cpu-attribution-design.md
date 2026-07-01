# DuckDB CPU Attribution — Design Spec

- **Status:** Proposed (for review)
- **Date:** 2026-06-26
- **Supersedes:** the caller-side CPU metric in PR #5642 (`kurrentdb.duckdb.cpu.seconds`, `source=caller`)
- **Spans two repositories:** Kurrent.Quack (executor + native interop) and KurrentDB (call-site migration + metric registration)

## 1. Background & problem

We want operators to be able to answer: **"What fraction of this node's CPU is DuckDB vs the rest of KurrentDB?"** Process- and system-level CPU are already exported (`kurrentdb_proc_cpu`, `kurrentdb_sys_cpu`); the missing piece is the DuckDB share so the two can be compared.

PR #5642 (open, not merged) proposed a first attempt: `DuckDBCpuMetrics.Measure(activity)` returns a `ref struct` scope that reads the calling thread's CPU (`clock_gettime(CLOCK_THREAD_CPUTIME_ID)` on Linux/macOS, `GetThreadTimes` on Windows) at construction and again at `Dispose`, recording the delta into a counter. It is wrapped around the synchronous DuckDB sections of commit, checkpoint, index reads, and query setup.

Code review identified three **fundamental** flaws, all rooted in one assumption — *"measure the calling thread across a synchronous span"*:

1. **Parallel work is invisible.** DuckDB executes parallelizable queries (scans, filters, aggregations, sorts) on its own morsel-scheduler worker threads. `CLOCK_THREAD_CPUTIME_ID` on the calling thread never sees those threads, so the metric undercounts most severely exactly when DuckDB is busiest. This is not an edge case — it is the common case for analytical queries.
2. **It blocks async.** The measurement requires start and end on the same thread within one synchronous region. If these operations ever become genuinely asynchronous (await points), the scope cannot span them — so the metric would actively prevent a desirable refactor.
3. **Thread-affinity of `ref struct` is incidental, not guaranteed.** Today a `ref struct` cannot cross an `await` or be captured, so it stays on one thread in practice. The language does not *guarantee* this, and the C# `ref struct` rules are actively loosening. If start and end ever run on different threads, the per-thread CPU delta is silently wrong — no error, just bad data.

No refinement of the `ref struct` rescues this; the measurement mechanism must change.

## 2. Goals & non-goals

**Goals**
- A **correct total** of DuckDB CPU consumed on the node, exported as a standard OpenTelemetry metric on `/metrics`.
- Correct in the presence of DuckDB's internal parallelism.
- Independent of caller threading and of whether callers are synchronous or asynchronous.
- Free of any "two readings on the same thread" assumption.

**Non-goals**
- **Per-query or per-activity attribution.** Measuring at the thread level means we cannot say whether a given worker's morsel belongs to a "commit" or a "query". We deliberately trade the (badly-estimated) activity breakdown of the old metric for a correct total. If per-query attribution is wanted later, DuckDB's own query profiling (`CPU_TIME`) is the right tool and is out of scope here (see Appendix).
- Changing what counts as "DuckDB work" — it remains everything executed through the DuckDB engine.

## 3. Chosen approach: a dedicated DuckDB executor

Stop measuring the caller. Instead, **own the threads DuckDB executes on, and measure those threads.**

A new **`DuckDBExecutor`** becomes the single place DuckDB runs. KurrentDB submits DuckDB operations to it and awaits results; the executor runs them on threads it creates, names, and measures. Because every byte of DuckDB CPU — parallel and serial — lands on owned threads, summing those threads' CPU yields the correct total, with no dependence on the caller's thread or sync/async shape.

This is the "own the worker pool" direction; it is the only option that captures parallel work, covers all activities (queries, commits, checkpoints, background), and is inherently async- and thread-safe.

## 4. Architecture

The executor owns two **distinct** sets of named threads:

- **Workers** (`N = SET threads`): each thread loops `duckdb_execute_tasks_state(sharedState)`, draining DuckDB's task/morsel queue. DuckDB is configured `SET threads = N; SET external_threads = N`, so these owned threads constitute DuckDB's *entire* parallel execution pool.
- **Dispatchers**: a bounded pool that runs the *issuing* side of an operation — the blocking `duckdb_query` / chunk-fetch / appender-flush call that drives the root pipeline. A caller submits an operation; a dispatcher executes it.

**The separation is the load-bearing invariant.** A thread blocked inside a query/flush call cannot also pump the task queue. If every thread were busy issuing operations, no thread would process morsels and DuckDB would deadlock. Workers and dispatchers must therefore be separate sets. The corollary is the deadlock-freedom guarantee in §8: because morsels are always drained by workers (never blocked by dispatchers), in-flight operations always make progress regardless of dispatcher saturation.

```
                         submit(op)                  results
   KurrentDB call sites ───────────▶  Dispatcher pool ───────▶ awaiting callers
   (await Execute)                    (issue + drive root          (any thread)
                                       pipeline; owned,
                                       named, measured)
                                            │
                                            │ enqueues tasks
                                            ▼
                                      DuckDB task queue
                                            │
                                            ▼
                                      Worker pool (N threads
                                      looping execute_tasks_state;
                                      owned, named, measured)

   metric = Σ CPU(all owned threads)   ← sampled on scrape
```

## 5. Execution model & API

**API (Quack):** a single async entry point, approximately:

```csharp
ValueTask<T> Execute<T>(DuckDBConnection conn, Func<DuckDBConnection, T> op, CancellationToken ct);
```

The caller awaits; the op is enqueued; a dispatcher runs `op(conn)`; the result completes the `ValueTask`; the caller resumes on its own context (which no longer matters for measurement).

**Connection affinity is preserved for free.** DuckDB forbids *concurrent* use of one connection, not use from different threads. KurrentDB already gives each unit of work its own connection — the shared write connection is used serially by the index processor; reads use per-request pooled connections — so two concurrent ops are never submitted for the same connection. A dispatcher simply borrows the connection for the duration of the op.

## 6. Call-site migration (KurrentDB)

The following sites change from inline synchronous DuckDB calls to `await executor.Execute(...)`:

- `DefaultIndexProcessor.Commit` and `UserIndexProcessor.Commit` / `Checkpoint` (appender flush).
- The reader path — `SecondaryIndexReaderBase.GetDbRecordsForwards/Backwards` and the category / event-type / user readers. These already sit under an async `ReadForwards` / `ReadBackwards`, so the async fits naturally.
- `QueryEngine.ExecuteAsync` and `GetArrowSchema`.
- The shutdown checkpoint in `DuckDBConnectionPoolLifetime.StopAsync`.

**The one genuinely tricky site is streaming reads.** `QueryEngine`'s consumer pulls chunks in a `TryRead` loop, and each `TryFetch` is a DuckDB call that must run on an owned thread. Resolution: run the whole consume loop *inside one `Execute` submission* (the loop executes on a dispatcher), rather than marshalling each fetch individually — one submission per query, every fetch owned. Treated as its own work item.

Most of the blast radius is converting a few `void`/synchronous DuckDB methods to async on call paths that are *already* async (`ReadForwards`, `ExecuteAsync`, the subscription loop).

## 7. The metric

- **Instrument:** an OpenTelemetry **observable counter** `kurrentdb.duckdb.cpu.seconds` (monotonic CPU-seconds), with an optional `role=worker|dispatcher` tag for diagnostics. On a dashboard, `rate(kurrentdb_duckdb_cpu_seconds_total[1m])` yields DuckDB CPU in cores; compare it against the process-CPU signal in the same unit. Note `kurrentdb_proc_cpu` (§1) is a gauge (an `ObservableUpDownCounter` of instantaneous CPU usage), so it must **not** be wrapped in `rate()` — divide by the gauge directly once both are expressed as cores (e.g. `rate(kurrentdb_duckdb_cpu_seconds_total[1m]) / kurrentdb_proc_cpu`, adjusting for the gauge's scaling).
- **Sampling:** on each scrape, sum every owned thread's *cumulative* CPU, read **by thread handle** (cross-thread, not "current thread"):
  - **Linux:** `pthread_getcpuclockid(thread, &clockid)` then `clock_gettime(clockid)`.
  - **Windows:** `GetThreadTimes(handle, …)` (kernel + user time) for any owned thread handle.
  - **macOS:** `thread_info(mach_thread, THREAD_BASIC_INFO, …)`. Dev-only platform; degrade to no-op if unavailable (macOS does not implement `pthread_getcpuclockid`).
  This generalizes the existing `ThreadCpuTime` shim from "the calling thread" to "any owned thread", and — crucially — reads correctly *even while a worker is parked inside `duckdb_execute_tasks_state`*, because the kernel keeps accounting that thread's CPU regardless. With ~8–16 owned threads sampled every 15s, the cost is negligible.
- **Ownership split:** Quack owns the threads and their handles and exposes their per-thread CPU (e.g., an enumeration of CPU-seconds per owned thread). KurrentDB registers the OTel observable counter that reads it and adds the `KurrentDB.DuckDB` meter to `metricsconfig.json`. OTel/config concerns stay in KurrentDB.
- **Testability:** keep an injectable CPU-time source (as the current implementation has) so the summing and tagging are deterministically unit-testable without relying on real per-thread accounting.

## 8. Error handling, cancellation & lifecycle

- **Op failures:** an exception thrown by `op(conn)` on a dispatcher is captured and faulted onto the returned `ValueTask`, surfacing to the awaiting caller exactly as a synchronous throw does today.
- **Cancellation:** the token wired through `Execute` triggers `connection.Interrupt` on the running query (today's `InterruptQueryOnCancellation` behavior); the op throws, the `ValueTask` faults, and the dispatcher is freed for the next op. DuckDB's `Interrupt` → `OperationCanceledException` mapping is retained.
- **No deadlock under load:** if every dispatcher is busy, further submissions queue (bounded) and wait. They cannot deadlock, because morsel processing happens on the worker pool, which is never blocked by dispatchers — so in-flight ops always complete and free dispatchers, draining the queue.
- **Thread lifecycle:** the executor owns its threads and task state (the creator owns teardown). Startup spawns workers (each looping `execute_tasks_state`) and dispatchers. Shutdown order: stop accepting new ops → drain in-flight → `duckdb_finish_execution(state)` so workers return from the native loop → join all threads → run the final checkpoint → dispose connections. The executor subsumes today's `DuckDBConnectionPoolLifetime` shutdown checkpoint.
- **Worker loss:** an uncaught native failure in a worker is logged and surfaced; remaining workers still drain the queue (degraded parallelism, not a hang). Dead threads are not resurrected — a crashing DuckDB worker indicates larger trouble.
- **Configuration:** `threads` (worker count; default to the existing core/RAM heuristic already used for `memory_limit`) and dispatcher count, both overridable.

## 9. Testing

**Quack (deterministic unit tests):**
- Injectable clock → CPU summing across owned threads is exact; the `role` tag is correct.
- **Headline test proving concern #1 is solved:** run a parallelizable query, then assert *total DuckDB CPU exceeds wall-clock elapsed*. That is only possible if multiple worker threads are counted — precisely what the caller-side approach could never show (it was bounded by single-thread wall time).
- **Async/thread-independence (concerns #2, #3):** drive an op whose continuation resumes on a different thread; assert the measurement is unaffected.
- **No deadlock under saturation:** submit more concurrent ops than dispatchers; assert all complete.
- **Lifecycle:** clean start/stop, `finish_execution` joins every thread, no leaked threads, shutdown checkpoint runs.
- **Failure & cancellation:** an op that throws faults the awaiting caller; a cancelled op interrupts DuckDB and frees its dispatcher.

**KurrentDB (functional safety net):** the existing SecondaryIndexing integration tests — reads, subscriptions, FlightSQL, query engine — must pass unchanged through the executor; that proves the call-site migration did not alter behavior. Plus a smoke check that `kurrentdb.duckdb.cpu.seconds` appears on `/metrics`.

## 10. Repository split, rollout & disposition of PR #5642

- **Kurrent.Quack (first):** add the `DuckDBExecutor` (worker + dispatcher pools), the native task-scheduler interop (`duckdb_create_task_state` / `execute_tasks_state` / `finish_execution` / `destroy_task_state`), the cross-thread per-OS CPU read, and the per-thread CPU enumeration. Ship as a new Quack version.
- **KurrentDB (second):** consume the new Quack version; migrate the DuckDB call sites (§6) to the executor; register the `kurrentdb.duckdb.cpu.seconds` observable counter and add the `KurrentDB.DuckDB` meter to `metricsconfig.json`; document the metric in `docs/server/diagnostics/metrics.md`.
- **PR #5642:** **remove** the caller-side CPU measurement (`DuckDBCpuMetrics`, `ThreadCpuTime`, the scope instrumentation and its tests). Per the review, we will not merge a metric whose headline value is wrong for parallel work. The `KurrentDB.DuckDB` meter name and the docs scaffolding may be retained as the landing point for this design. (If #5642 has other unrelated value it can keep it; otherwise it closes in favor of this work.)

## 11. Risks & open questions

- **Dispatcher pool sizing.** Too few dispatchers throttle concurrent reads; too many add scheduling overhead against a fixed worker pool. Needs a sensible default and load testing. (Does not affect correctness of the metric, only read latency.)
- **Worker / external-threads interaction under concurrent queries.** Multiple in-flight queries share the single worker pool via DuckDB's global task scheduler. Behavior is expected to be standard, but must be load- and soak-tested before this becomes the default execution path — it changes DuckDB's execution model from internal to external threads.
- **macOS per-thread CPU.** `pthread_getcpuclockid` is unsupported on macOS; the `thread_info`/mach path must be implemented or the metric degrades to no-op on macOS (acceptable: macOS is a development platform only).
- **Magnitude of the old blind spot.** The headline test (total CPU > wall-clock) will, for the first time, quantify how much CPU the previous caller-side metric was missing — useful validation that the rework was warranted.
- **Scope of the async migration.** Converting the streaming reader to run its consume loop on a dispatcher is the largest single change; it must preserve current read semantics (ordering, cancellation, snapshot capture).

## Appendix: verified facts (DuckDB 1.5, as shipped)

- **Task-scheduler C API present** in the shipped `libduckdb` 1.5 binaries (Linux/Windows/macOS): `duckdb_create_task_state`, `duckdb_execute_tasks`, `duckdb_execute_tasks_state`, `duckdb_execute_n_tasks_state`, `duckdb_finish_execution`, `duckdb_task_state_is_finished`, `duckdb_destroy_task_state`, `duckdb_execution_is_finished`.
- **`duckdb_execute_tasks_state` semantics** (DuckDB C API docs): "Execute DuckDB tasks on this thread. The thread will keep on executing tasks forever, until `duckdb_finish_execution` is called on the state. Multiple threads can share the same `duckdb_task_state`." This is exactly the owned-worker-pool primitive.
- **`external_threads` + `threads`** caps total parallelism on DuckDB's global task scheduler; combined with externally-provided threads it replaces the internal pool.
- **DuckDB threads do not name themselves**, so a sampler cannot reliably identify DuckDB's internal workers from outside — which is *why* owning (and naming) the threads is necessary, and why a thread-enumeration sampler was rejected.
- **DuckDB profiling `CPU_TIME`** (the per-query alternative, deliberately out of scope) "measures the CPU time spent on a query, specifically accumulating operator timings; it does not account for parsing or planning." It is per-query, requires profiling to be enabled (overhead), and does not cover non-query work (appender flush / commit / checkpoint). The C symbol `duckdb_get_profiling_info` is present in 1.5.
- **Kurrent.Quack today** exposes neither the task scheduler nor profiling; its `Threading` namespace is the buffered appender only. Both require new Quack work.
