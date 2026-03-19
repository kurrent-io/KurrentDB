# ARM64 Platform Considerations

This document captures the key considerations for running KurrentDB on ARM hardware (aarch64/ARM64).

## 1. DuckDB Native Library (Secondary Indexing) — Highest Risk

Secondary indexing depends on DuckDB via `Kurrent.Quack` and `Kurrent.Surge.DuckDB`. The project explicitly suppresses `DuckDBNET001` warnings (`src/KurrentDB.SecondaryIndexing/KurrentDB.SecondaryIndexing.csproj`), which signals platform/architecture compatibility concerns. Verify that DuckDB.NET ships ARM64 native binaries for the target OS, or secondary indexing (enabled by default in 25.1) won't work.

## 2. Docker & CI Pipeline — Only x64 Today

The deployment pipeline is hardcoded to x64:

- `Dockerfile` defaults to `RUNTIME=linux-x64`
- `.github/workflows/build-container-reusable.yml` maps only to `linux-x64` and `linux-musl-x64`
- `.github/workflows/common.yml` uses `RUNTIME=linux-amd64`

ARM64 container images, a multi-arch build strategy (e.g., `docker buildx`), and CI runners on ARM or cross-compilation support will be needed.

## 3. Build Configuration Gaps

`src/Directory.Build.props` already declares `ARM64` as a supported platform, and an ARM64 CI workflow exists (`.github/workflows/build-ubuntu-20.04-arm64.yml`). However:

- Six plugin `.csproj` files have `Release|x64`-only property groups for debug symbols — ARM64 builds won't get matching debug symbol settings.
- The `protoc` compiler (for gRPC code generation) has known issues on ARM — five `.csproj` files carry comments about `protoc` path failures even on macOS x64. Building on ARM hosts needs a working `Grpc.Tools` with ARM64 protoc binaries (available since Grpc.Tools 2.50+).

## 4. ARM64 Weak Memory Model — Correctness Risk

ARM64 has a **weak memory model** compared to x86's Total Store Order (TSO). On x86, stores become visible to other cores in program order. On ARM64, the CPU may reorder reads and writes more aggressively. Code that works on x86 "by accident" — without explicit memory barriers — can have **visibility bugs** on ARM64 (e.g., a thread spinning on a stale cached value indefinitely).

### What the codebase does well

Most critical paths already use proper synchronization. `Volatile.Read()` and `Volatile.Write()` are used correctly in secondary indexing (`DefaultIndexInFlightRecords`). `Interlocked` operations are used extensively for checkpoints, counters, and queue management (`TableIndex`, `TFChunk`, `ReplicationTrackingService`, `ThreadPoolMessageScheduler`). The `volatile` keyword is applied to key fields in `TcpConnection`, `TFChunk`, `StorageChaser`, and `LeaderReplicationService`. `Thread.MemoryBarrier()` is used in `TFChunk` and `DynamicCacheManager`.

### Findings

We identified 16 fields across 10 files where a value-type or enum field is written on one thread and read on another without `volatile`, `Interlocked`, or a lock. On x86 these work due to TSO guarantees; on ARM64 they may not. Every instance has been tagged with an `// ARM64-UNSAFE:` comment in the source explaining the specific cross-thread access pattern. To find them all:

```
grep -rn "ARM64-UNSAFE" src/
```

The affected areas are core services (replication tracking, persistent subscriptions, leader replication, HTTP service, request management), TCP transport (connection base, SSL connections, connection manager), and HTTP controllers (info endpoint).

### Why we believe the list is complete

The audit covered every `.cs` file under `src/` across multiple passes with progressively wider scope:

1. **All `private bool` fields** in every class implementing `IHandle<>` or spawning background threads — the most common pattern for cross-thread signaling flags.
2. **All `private int`, `private long`, and enum fields** (including `VNodeState`) in the same classes — same visibility risk as booleans.
3. **Reference-type fields** — checked for publication safety issues where one thread constructs an object and assigns it to a shared field without a barrier (the reader could see the reference but not the object's initialized state).
4. **Static mutable fields** — searched for `static` non-`readonly` non-`const` fields across the codebase.
5. **DateTime/TimeSpan fields** — value types with the same ordering concerns.
6. **Delegate and Action fields** — assigned on one thread, invoked on another.
7. **CancellationTokenSource fields** — stored in a field, cancelled from one thread, token checked from another.
8. **The Projections system** — has its own threading model (`CoreProjection` state machine) but is single-threaded per projection, so no cross-thread field access.

Everything not flagged falls into one of these safe categories:
- Already marked `volatile` (e.g., `TcpConnection._isClosed`, `TFChunk` cache fields, `StorageChaser._systemStarted`, `LeaderReplicationService._state`).
- Accessed only through `Interlocked` operations (e.g., `TableIndex` checkpoints, `TFChunk` sizes, `ThreadPoolMessageScheduler` counters).
- Protected by a `lock` at every access site (e.g., TCP connection send/receive/close locks, `RequestManagerBase._prepareLogPositions`).
- Confined to a single thread — most `VNodeState` fields in `StorageWriterService`, `ReplicaService`, and `ClusterVNodeController` are only accessed on the same message queue thread, which was verified by tracing callers.

### Recommended fixes

- Add `volatile` to the flagged fields, or replace bare reads/writes with `Volatile.Read()`/`Volatile.Write()` calls.
- For compound state (e.g., `RequestManagerBase` with multiple coordinated booleans), consider collapsing into a single `int` with `Interlocked` operations.
- Replace `Thread.MemoryBarrier()` in `DynamicCacheManager` with targeted `Volatile` or `Interlocked` operations — full fences are more expensive on ARM64 than on x86.

## 5. Performance Characteristics

The codebase has no SIMD intrinsics (SSE/AVX), and all unsafe code uses portable `sizeof()` and `nint`. Performance tuning will differ on ARM64:

- **Server GC** (enabled by default) may behave differently on ARM core topologies (big.LITTLE, different cache hierarchies).
- **Hash functions** (XXHash, Murmur2/3 in `src/KurrentDB.Core/Index/Hashes/`) are CPU-intensive — benchmark on ARM to check for regressions.
- **StreamInfoCacheCapacity** (default 100K) and chunk caching may need re-tuning for ARM memory subsystems.
- .NET 10 JIT on ARM64 is mature but hot paths (index lookups, replication) should be verified for latency.

## 6. Third-Party / Native Dependencies

Audit all NuGet packages that ship native binaries for ARM64 support:

- **gRPC native transport** — Grpc.Core (if used) vs grpc-dotnet (managed, ARM-safe).
- **Encryption at Rest** (`KurrentDB.Security.EncryptionAtRest`) — check for native crypto library dependencies.
- **LDAP authentication** (`KurrentDB.Auth.Ldaps`) — underlying native LDAP bindings.
- **Licensed plugins** (TCP plugin, Archiving with S3) that may bundle platform-specific code.

Check `Directory.Packages.props` for the full dependency list and verify each native dependency publishes ARM64 variants.

## Summary

The C# code is **structurally portable** (no SIMD, proper `sizeof()` and `nint` usage), but the ARM64 weak memory model introduces correctness risks in several unprotected boolean flags used for cross-thread signaling. These must be audited and fixed before running on ARM hardware. Beyond that, the main work is in the build/deployment pipeline, native dependency verification (especially DuckDB), and performance validation. The existing ARM64 CI workflow in GitHub Actions provides a foundation to build on.
