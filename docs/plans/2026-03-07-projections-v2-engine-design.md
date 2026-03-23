# Projections v2 Engine Design

**Date**: 2026-03-07
**Status**: Proposed

## Overview

A new projections processing engine that replaces the current complex pipeline with simpler, modern infrastructure already available in KurrentDB: filtered read-all enumerators, secondary indexes (DuckDB), and multi-stream atomic appends.

The new engine eliminates multi-stream readers, event reordering, per-stream emitted-stream tracking, and complex idempotency checks. It introduces partitioned parallel processing with Chandy-Lamport checkpointing and atomic output writes.

## Goals

- Simplify the projections processing pipeline by leveraging existing database infrastructure
- Enable parallel event processing via configurable partitions
- Achieve atomic checkpointing (checkpoint + emitted events + state in one write)
- Eliminate internal link event overhead (partition catalogs, all-results streams)
- Maintain backward compatibility with existing projection JavaScript API and output streams

## Non-Goals

- System projections — these are being replaced by DuckDB secondary indexes
- Changes to the JavaScript runtime (`IProjectionStateHandler` / Jint)
- Changes to the management layer API (HTTP/gRPC endpoints)

## Read Strategies

Three read strategies replace the current reader hierarchy (`TransactionFileEventReader`, `MultiStreamEventReader`, `EventByTypeIndexEventReader`, `StreamEventReader`):

### Strategy A — Filtered $all (default)

Used by `fromAll()`, `fromCategory()`, `fromEventType()`.

Uses the existing `Enumerator.AllSubscriptionFiltered` with the appropriate `IEventFilter`:
- `fromAll()` → `DefaultAllFilter` or user-specified filter
- `fromCategory(name)` → `StreamName.Prefixes("name-")`
- `fromEventType(type)` → `EventType.Prefixes("type")`

Checkpoints by log position (`TFPos`). Delivers events in log order with catch-up and live tailing phases.

### Strategy B — Direct Stream Read

Used by `fromStream('stream-name')`.

Reads from the primary index via direct stream read — no log scan needed. This is the most efficient path for single-stream projections.

**Enhancement required**: Stream reads currently don't support starting from a log position. This needs to be added so that checkpointing remains uniform across all strategies. The DuckDB default index (`$idx-all`) can resolve between stream event number and log position.

Checkpoints by log position.

### Strategy C — DuckDB Stream-Name-Set Filter

Used by `fromStreams(['stream-a', 'stream-b', ...])`.

Queries the DuckDB default index with:
```sql
SELECT * FROM "$idx-all"
WHERE stream IN ('stream-a', 'stream-b', ...)
  AND log_position > @checkpoint
ORDER BY log_position
```

Returns events in log order naturally. Checkpoints by log position.

**Enhancement required**: A new `IEventFilter` implementation for stream-name-set matching, backed by DuckDB queries.

### Uniform Interface

All three strategies produce events ordered by log position and checkpoint using log position. This gives a single downstream interface regardless of source configuration.

## Processing Pipeline

```
Reader (Strategy A/B/C)
  │
  │  events in log-position order
  ▼
Dispatcher (consistent hash of partitionBy key)
  │
  ├──► Partition Channel 0  ──► Process ──► Output Buffer 0
  ├──► Partition Channel 1  ──► Process ──► Output Buffer 1
  ├──► Partition Channel 2  ──► Process ──► Output Buffer 2
  ...
  └──► Partition Channel N-1 ──► Process ──► Output Buffer N-1
  │
  ▼
Checkpoint Coordinator (Chandy-Lamport markers)
  │
  ▼
Multi-Stream Atomic Append (checkpoint + emitted events + state)
```

### Dispatcher

The dispatcher receives events from the reader and routes them to partition channels:

1. Call `partitionBy(event)` to get the partition key (or use stream name as default)
2. Compute `hash(partitionKey) % N` to select the target partition channel
3. Write the event to that partition's `System.Threading.Channel<T>`

Events for the same partition key always go to the same partition, preserving per-key ordering.

### Partition Processing

Each partition runs a dedicated async task that:

1. Reads events from its channel sequentially
2. Loads partition state from cache (or from stream on cache miss)
3. Calls `IProjectionStateHandler.ProcessEvent()` (existing Jint runtime, unchanged)
4. Collects emitted events and state updates into the current output buffer

The partition count is configurable per projection (default TBD, e.g., 4). The existing `IProjectionStateHandler` and Jint JavaScript runtime are reused without modification.

### Backpressure

Bounded channel capacity on partition channels provides natural backpressure. If partitions are slow, the dispatcher blocks on `channel.Writer.WriteAsync()`, which in turn pauses the reader. No explicit backpressure signaling needed.

### No Reordering

The filtered $all enumerator delivers events in log order. Within a partition, events for the same key are processed in order. Cross-partition ordering is not required because partitions process independent keys. This eliminates the current `StagedProcessingQueue` and its complex ordering logic.

## Checkpointing via Chandy-Lamport

The processing topology is a simple single-producer (reader/dispatcher) to N-consumer (partitions) fan-out. The Chandy-Lamport algorithm provides consistent distributed snapshots without stalling the pipeline.

### Protocol

1. **Trigger**: Checkpoint interval reached (configurable by byte threshold, event count, or time interval).

2. **Inject markers**: The dispatcher injects a `CheckpointMarker(sequenceNumber, logPosition)` into each of the N partition channels. The `logPosition` is the position of the last event dispatched before the marker.

3. **Partition snapshot**: When a partition processes the marker, it:
   - Freezes its current output buffer (emitted events + dirty state)
   - Reports the frozen buffer to the checkpoint coordinator
   - Switches to a fresh output buffer (double-buffered) and continues processing

4. **Coordinator collects**: Once all N partitions have reported for the same marker sequence number, the coordinator has a consistent snapshot.

5. **Atomic write**: The coordinator issues a single multi-stream append (`ClientMessage.WriteEvents` with multiple `EventStreamIds`) containing:
   - Checkpoint event → `$projections-{name}-checkpoint` stream (with log position)
   - All emitted events from the N frozen output buffers → their respective target streams
   - All dirty partition state updates → `$projections-{name}-{partitionKey}-result` streams

6. **Completion**: On successful write, the frozen buffers are released.

### Constraints

- **At most 1 outstanding checkpoint**: A new marker is not injected until the previous checkpoint write completes. This bounds memory usage and ensures buffers don't accumulate.
- **2 buffers per partition**: One being written (frozen), one being filled (active). This is sufficient given the single-outstanding-checkpoint constraint.
- **No pipeline stall**: Partitions continue processing into their fresh buffer while the coordinator writes the previous checkpoint. The only wait is if a second checkpoint triggers before the first completes (bounded by channel capacity).

### Why Chandy-Lamport

The alternative — pausing all partitions, collecting state, writing, then resuming — creates unnecessary pipeline stalls. Chandy-Lamport markers flow through the same channels as events, so each partition reaches the snapshot point naturally without coordinated pauses. The simple topology (single source → N channels) makes the algorithm straightforward: only forward-channel markers are needed, no cross-partition marker propagation.

## Output and Emission

### Kept (backward compatible)

- `emit(streamName, eventType, data)` → writes events to target streams
- `linkTo(streamName, event)` → writes link events to target streams
- Per-partition state → `$projections-{name}-{partitionKey}-result` streams
- `Result` and `ResultRemoved` event types in state streams

### Eliminated

- **Partition catalog links**: Current engine creates link events in `$projections-{name}-partitions` for every new partition discovered. Eliminated.
- **All-results stream links**: Current engine creates `$projections-{name}-result` with links to every result event. Eliminated.
- **Any other internally-generated link events**: Only user-specified `linkTo()` creates link events.

This significantly reduces write amplification. If partition discovery is needed, it can be served by querying the DuckDB category index on result streams.

### Atomic Output

All output from a checkpoint interval is written in a single multi-stream append:

```
WriteEvents {
  EventStreamIds: [
    "$projections-{name}-checkpoint",
    "target-stream-1",
    "target-stream-2",
    "$projections-{name}-partitionA-result",
    "$projections-{name}-partitionB-result",
    ...
  ],
  ExpectedVersions: [...],
  Events: [checkpointEvent, emittedEvent1, emittedEvent2, stateA, stateB, ...],
  EventStreamIndexes: [0, 1, 1, 2, 3, ...]
}
```

This guarantees all-or-nothing semantics: either the entire checkpoint (with all emitted events and state) is committed, or nothing is.

## State Management

### In-Memory Cache

Each partition maintains an LRU cache of partition states for its assigned keys. Cache sizing is configurable. On cache miss, state is loaded from the partition's result stream.

### Persistence

Dirty partition states are included in the atomic checkpoint write. State is written to `$projections-{name}-{partitionKey}-result` streams as `Result` events.

### Recovery

On startup or after crash:

1. Read the last checkpoint from `$projections-{name}-checkpoint` to get the log position
2. Resume the reader from that log position
3. Partition states are loaded on-demand from their result streams (cache starts cold)
4. No idempotency checks needed — replay starts from the exact committed checkpoint position

The atomic checkpoint write guarantees that if the checkpoint exists, all associated emitted events and state updates also exist. If the write was interrupted, the checkpoint doesn't exist, and we replay from the previous one.

## Integration Architecture

```
ProjectionManager (existing, unchanged)
  │
  ├──► ManagedProjection (existing state machine)
  │       │
  │       ├──► [engine=v1] ──► CoreProjection (existing pipeline)
  │       │
  │       └──► [engine=v2] ──► ProjectionEngineV2 (new)
  │
  └──► Management HTTP/gRPC endpoints (unchanged)
```

### Routing

- A per-projection configuration flag (`engine: v1 | v2`) controls which engine processes the projection
- New projections default to `v2`
- Existing projections continue on `v1` until explicitly migrated
- The `ManagedProjection` state machine is reused with minimal changes (routing logic added)
- The `ProjectionManager` and management endpoints remain unchanged

### New Service: ProjectionEngineV2

A new service class that:
- Receives start/stop/reset commands from `ManagedProjection`
- Creates the appropriate read strategy based on projection source definition
- Manages the dispatcher, partition channels, and checkpoint coordinator
- Reports status back to `ManagedProjection` (running, faulted, stopped, etc.)

### System Projections

System projections (`$by_category`, `$by_event_type`, `$stream_by_category`, `$streams`) are **not handled** by the new engine. DuckDB secondary indexes provide equivalent functionality and are the intended replacement. System projections remain on the v1 engine during the transition period and will be deprecated.

## Error Handling

### Projection Faults

A JavaScript error in any partition faults the entire projection. The projection enters the `Faulted` state with the error details. The user can inspect the error and reset/restart the projection. This matches current behavior.

### Crash Recovery

The atomic checkpoint guarantees consistency. On crash:
- If the last checkpoint write completed: all emitted events and state are present, resume from checkpoint
- If the last checkpoint write was interrupted: it's as if it never happened, resume from the previous checkpoint

No WAL, no replay log, no idempotency checks needed.

### Backpressure and Timeouts

- Bounded channels provide backpressure from partitions to reader
- The reader uses `IExpiryStrategy` (default 7-second timeout with retry) for read operations
- If a partition is consistently slow, the entire pipeline slows down proportionally (acceptable given uniform partition load)

## Components Eliminated

The following current-engine components are **not needed** in v2:

| Current Component | Replacement |
|---|---|
| `EventReaderCoreService` | Read strategies using existing enumerators |
| `TransactionFileEventReader` | `Enumerator.AllSubscriptionFiltered` |
| `MultiStreamEventReader` | DuckDB stream-name-set filter |
| `EventByTypeIndexEventReader` | `Enumerator.AllSubscriptionFiltered` with event type filter |
| `StreamEventReader` | Direct stream read (enhanced with log position start) |
| `HeadingEventReader` | Not needed (enumerators handle live tailing) |
| `StagedProcessingQueue` | Partitioned channels (no reordering needed) |
| `CommittedEventWorkItem` (5-stage pipeline) | Simple sequential per-partition processing |
| `ProjectionCheckpoint` (per-stream tracking) | Atomic multi-stream append |
| `EmittedStream` / `EmittedStreamsWriter` | Output buffers + multi-stream append |
| `ResultEventEmitter` (link events) | Direct result events only |
| `ReaderSubscriptionBase` | Not needed (no subscription-to-reader indirection) |
| `EventProcessingProjectionProcessingPhase` | `ProjectionEngineV2` processing loop |

## Required Enhancements to Existing Infrastructure

1. **Stream read from log position**: Enable `ReadStreamEventsForward` to accept a starting log position instead of only stream event number. Use DuckDB index for resolution.

2. **Stream-name-set EventFilter**: New `IEventFilter` implementation that matches events whose stream name is in a specified set. Backed by DuckDB query on `$idx-all`.

3. **ManagedProjection routing**: Add engine version config flag and routing logic to delegate to v1 (`CoreProjection`) or v2 (`ProjectionEngineV2`).

## Migration Path

1. **Phase 1**: Implement `ProjectionEngineV2` alongside existing engine. New projections default to v2. Existing projections stay on v1.
2. **Phase 2**: Provide migration tooling to move existing projections from v1 to v2 (reset + replay with new engine).
3. **Phase 3**: Deprecate v1 engine. Deprecate system projections (replaced by secondary indexes).
4. **Phase 4**: Remove v1 engine and system projections.
