# KurrentDB Projections Core Architecture

This document describes the internal architecture of the projections subsystem in KurrentDB, covering management endpoints, event processing flow, reader infrastructure, and event emission.

---

## Table of Contents

1. [Projections Management Endpoints and Flow](#1-projections-management-endpoints-and-flow)
2. [Event Processing Pipeline](#2-event-processing-pipeline)
3. [Readers and Event Retrieval](#3-readers-and-event-retrieval)
4. [How Projections Produce New Events](#4-how-projections-produce-new-events)

---

## 1. Projections Management Endpoints and Flow

### 1.1 HTTP Endpoints

Defined in `src/KurrentDB.Projections.Core/Services/Http/ProjectionsController.cs`. All routes are registered in `SubscribeCore()`:

| Method | Route | Operation |
|--------|-------|-----------|
| GET | `/projections` | List all projections |
| POST | `/projections/restart` | Restart projection subsystem |
| GET | `/projections/any` | List all projections (any mode) |
| GET | `/projections/all-non-transient` | List non-transient projections |
| GET | `/projections/transient` | List transient projections |
| GET | `/projections/onetime` | List one-time projections |
| GET | `/projections/continuous` | List continuous projections |
| POST | `/projections/transient` | Create transient projection |
| POST | `/projections/onetime` | Create one-time projection |
| POST | `/projections/continuous` | Create continuous projection |
| GET | `/projection/{name}/query` | Get projection query/definition |
| PUT | `/projection/{name}/query` | Update projection query |
| GET | `/projection/{name}` | Get projection status |
| DELETE | `/projection/{name}` | Delete projection |
| GET | `/projection/{name}/statistics` | Get projection statistics |
| GET | `/projection/{name}/state` | Get projection state (partition optional) |
| GET | `/projection/{name}/result` | Get projection result (partition optional) |
| POST | `/projection/{name}/command/disable` | Disable projection |
| POST | `/projection/{name}/command/enable` | Enable projection |
| POST | `/projection/{name}/command/reset` | Reset projection |
| POST | `/projection/{name}/command/abort` | Abort projection |
| POST | `/projection/{name}/config` | Update projection config |
| GET | `/projection/{name}/config` | Get projection config |

### 1.2 gRPC Endpoints

Defined across partial classes in `src/KurrentDB.Projections.Core/Services/Grpc/ProjectionManagement.*.cs`:

- `Create` — Create a new projection
- `Update` — Update projection query/config
- `Delete` — Delete a projection
- `Enable` — Enable a projection
- `Disable` — Disable a projection
- `Reset` — Reset a projection
- `RestartSubsystem` — Restart the projection subsystem
- `Result` — Get projection result
- `Statistics` — Get projection statistics

Proto definition: `src/Protos/Grpc/projections.proto`. The `Statistics` RPC is server-streaming (`stream StatisticsResp`), all others are unary. The `Disable` RPC supports a `write_checkpoint` option.

The gRPC service (`ProjectionManagement`) publishes `ProjectionManagementMessage.Command.*` messages onto the internal bus, the same messages used by the HTTP controller.

### 1.3 Management Message Types

All defined in `src/KurrentDB.Projections.Core/Messages/ProjectionManagementMessage.cs`:

**Command messages** (requests from API):
- `Command.Post` — Create a new projection (with mode, name, handler type, query, emit settings)
- `Command.PostBatch` — Create multiple projections atomically
- `Command.UpdateQuery` — Update query text and emit settings
- `Command.Delete` — Delete with options for checkpoint/state/emitted stream cleanup
- `Command.Enable` / `Command.Disable` / `Command.Abort` / `Command.Reset`
- `Command.GetStatistics` / `Command.GetState` / `Command.GetResult` / `Command.GetQuery`
- `Command.GetConfig` / `Command.UpdateConfig`

**Response messages**:
- `Updated`, `Statistics`, `ProjectionState`, `ProjectionResult`, `ProjectionQuery`, `ProjectionConfig`
- `OperationFailed`, `NotFound`, `NotAuthorized`, `Conflict`

### 1.4 Management Architecture and Flow

```
┌─────────────────────┐     ┌──────────────────────┐
│  HTTP Controller    │     │  gRPC Service         │
│  ProjectionsController│   │  ProjectionManagement │
└────────┬────────────┘     └────────┬─────────────┘
         │                           │
         │  ProjectionManagementMessage.Command.*
         ▼                           ▼
┌─────────────────────────────────────────────────┐
│              ProjectionManager                   │
│  (Services/Management/ProjectionManager.cs)      │
│                                                   │
│  - Handles all Command.* messages                 │
│  - Maintains Dictionary<string, ManagedProjection>│
│  - Persists state to $projections-$master stream  │
│  - Routes to appropriate ManagedProjection        │
└─────────────────────┬───────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│           ManagedProjection                      │
│  (Services/Management/ManagedProjection.cs)      │
│                                                   │
│  - State machine per projection                   │
│  - Persists config to $projections-{name} stream  │
│  - Publishes CoreProjectionManagementMessage.*    │
│    to ProjectionCoreService via worker queues     │
└─────────────────────┬───────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│          ProjectionCoreService                   │
│  (Services/Processing/ProjectionCoreService.cs)  │
│                                                   │
│  - Creates/manages CoreProjection instances       │
│  - Handles CreateAndPrepare, Start, Stop, Kill   │
│  - Maintains Dictionary<Guid, CoreProjection>     │
└─────────────────────────────────────────────────┘
```

### 1.5 ManagedProjection State Machine

States defined in `ManagedProjectionState` enum:

```
Creating → Loading → Loaded → Preparing → Prepared → Starting → Running
                                                         ↓
                                               Stopping → Stopped
                                               Aborting → Aborted
                                                         → Completed
                                                         → Faulted
                                                         → Deleting
                                               LoadingStopped
```

Each state has a corresponding handler class in `Services/Management/ManagedProjectionStates/`:
- `CreatingLoadingLoadedState` — Handles initial loading from persisted state
- `PreparingState` / `PreparedState` — Projection definition compiled, waiting to start
- `StartingState` — Sending start command to core
- `RunningState` — Actively processing events
- `StoppingState` / `StoppedState` — Graceful shutdown
- `AbortingState` / `AbortedState` — Forced shutdown
- `CompletedState` — One-time/query projection finished
- `FaultedState` — Error occurred
- `DeletingState` — Being removed

### 1.6 Projection Modes

- **Transient** — Ad-hoc queries, ephemeral, no persistence. Can be created/controlled by normal users.
- **OneTime** — One-time batch projections with optional checkpoints. Stops at EOF.
- **Continuous** — Long-running, persistent projections with checkpoints. Requires Admin/Operations role.

### 1.7 Projection Configuration Parameters

Configurable via `Command.UpdateConfig` or at creation time:

| Parameter | Description | Default |
|-----------|-------------|---------|
| `EmitEnabled` | Allow event emission | false |
| `TrackEmittedStreams` | Record emitted stream names for cleanup | false |
| `CheckpointAfterMs` | Min time between checkpoints (ms) | from `ProjectionConsts` |
| `CheckpointHandledThreshold` | Checkpoint after N events handled | from `ProjectionConsts` |
| `CheckpointUnhandledBytesThreshold` | Checkpoint after N bytes unhandled | from `ProjectionConsts` |
| `PendingEventsThreshold` | Max pending events before backpressure | from `ProjectionConsts` |
| `MaxWriteBatchLength` | Max events per write batch | from `ProjectionConsts` |
| `MaxAllowedWritesInFlight` | Max concurrent write operations | from `ProjectionConsts` |
| `ProjectionExecutionTimeout` | JS execution timeout (ms) | from config |

### 1.8 Worker Queue Distribution

The `ProjectionManager` distributes projections across worker queues (`_queues`). Each worker queue runs its own `ProjectionCoreService` and `EventReaderCoreService`. The `ProjectionManagerMessageDispatcher` routes `CoreProjectionStatusMessage.*` responses back to the `ProjectionManager` from worker threads.

---

## 2. Event Processing Pipeline

### 2.1 High-Level Flow

```
                    ┌────────────────────┐
                    │   Event Store      │
                    │   (TF / Streams)   │
                    └────────┬───────────┘
                             │ Read operations
                             ▼
                    ┌────────────────────┐
                    │   EventReader      │
                    │ (Stream/TF/Multi/  │
                    │  EventByType)      │
                    └────────┬───────────┘
                             │ ReaderSubscriptionMessage
                             │ .CommittedEventDistributed
                             ▼
                    ┌────────────────────┐
                    │EventReaderCoreService│
                    │  routes to          │
                    │  IReaderSubscription │
                    └────────┬───────────┘
                             │
                             ▼
                    ┌────────────────────┐
                    │ ReaderSubscription  │
                    │ (filtering, tagging,│
                    │  checkpoint suggest)│
                    └────────┬───────────┘
                             │ EventReaderSubscriptionMessage
                             │ .CommittedEventReceived
                             ▼
                    ┌────────────────────────────┐
                    │ ReaderSubscriptionDispatcher│
                    │  (routes by subscriptionId) │
                    └────────┬───────────────────┘
                             │
                             ▼
              ┌──────────────────────────────────┐
              │EventSubscriptionBasedProjection   │
              │ProcessingPhase                    │
              │(EventProcessingProjectionPhase)   │
              │                                    │
              │  → Creates CommittedEventWorkItem  │
              │  → Enqueues to CoreProjectionQueue │
              └──────────────┬───────────────────┘
                             │
                             ▼
              ┌──────────────────────────────────┐
              │    StagedProcessingQueue          │
              │    (multi-stage work items)        │
              │                                    │
              │  Stage 0: RecordEventOrder         │
              │  Stage 1: GetStatePartition        │
              │  Stage 2: Load (partition state)   │
              │  Stage 3: ProcessEvent             │
              │  Stage 4: WriteOutput              │
              └──────────────┬───────────────────┘
                             │
                             ▼
              ┌──────────────────────────────────┐
              │  IProjectionStateHandler          │
              │  (JS runtime / native handler)    │
              │                                    │
              │  → ProcessEvent() returns:         │
              │    - newState                       │
              │    - emittedEvents[]                │
              │    - projectionResult               │
              └──────────────────────────────────┘
```

### 2.2 CoreProjection State Machine

`CoreProjection` (`Services/Processing/CoreProjection.cs`) manages the lifecycle of a single running projection instance:

```
Initial → LoadStateRequested → StateLoaded → Subscribed → Running
                                                            │
                                              ┌─────────────┼──────────────┐
                                              ▼             ▼              ▼
                                          Stopping    FaultedStopping  CompletingPhase
                                              ▼             ▼              ▼
                                          Stopped       Faulted       PhaseCompleted
                                                                          │
                                                                   (next phase or Stop)
```

Key transitions:
1. **Initial**: Initialize caches, checkpoint reader, and emitted stream trackers
2. **LoadStateRequested**: `CheckpointReader.BeginLoadState()` reads from the checkpoint stream
3. **StateLoaded**: Checkpoint loaded; `BeginPhase()` initializes the processing phase
4. **Subscribed**: Reader subscription established; transitions to Running
5. **Running**: `ProcessingPhase.ProcessEvent()` is called via tick mechanism
6. **CompletingPhase**: For multi-phase projections (query then write results)
7. **PhaseCompleted**: Moves to next phase or stops

### 2.3 Processing Phases

Defined in `Services/Processing/Phases/`:

- **`EventProcessingProjectionProcessingPhase`** — The main phase that processes events through the state handler. Handles `CommittedEventReceived` messages and creates `CommittedEventWorkItem` instances.

- **`WriteQueryEofProjectionProcessingPhase`** — Writes final results when a query reaches EOF.

- **`WriteQueryResultProjectionProcessingPhase`** — Writes intermediate/final results for queries.

Phase state enum (`PhaseState`): `Unknown`, `Stopped`, `Starting`, `Running`

Subscription state enum (`PhaseSubscriptionState`): `Unknown`, `Unsubscribed`, `Subscribing`, `Subscribed`, `Failed`

### 2.4 Processing Strategies

`ProcessingStrategySelector` (`Services/Processing/Strategies/ProcessingStrategySelector.cs`) creates the appropriate strategy:

- **`QueryProcessingStrategy`** — For one-time/transient projections (`StopOnEof = true`). Creates two phases: event processing + write EOF results.
- **`ContinuousProjectionProcessingStrategy`** — For continuous projections. Creates a single `EventProcessingProjectionProcessingPhase` that runs indefinitely.

Both extend `EventReaderBasedProjectionProcessingStrategy` → `DefaultProjectionProcessingStrategy` → `ProjectionProcessingStrategy`.

### 2.5 CommittedEventWorkItem Stages

The `CommittedEventWorkItem` (`Services/Processing/WorkItems/CommittedEventWorkItem.cs`) processes through these stages in the `StagedProcessingQueue`:

1. **RecordEventOrder** — Records the event ordering for consistency
2. **GetStatePartition** — Calls `StatePartitionSelector.GetStatePartition()` to determine which partition this event belongs to (e.g., stream name for `foreachStream`)
3. **Load** — Loads the partition state from cache or reads from the event store via `BeginGetPartitionStateAt()`
4. **ProcessEvent** — Calls `IProjectionStateHandler.ProcessEvent()` (JS runtime) which returns new state and emitted events
5. **WriteOutput** — Calls `FinalizeEventProcessing()` which writes results, accounts partitions, and emits events through the `ResultWriter`

### 2.6 The Tick Mechanism

`CoreProjection.EnsureTickPending()` publishes a `ProjectionCoreServiceMessage.CoreTick` that triggers `CoreProjection.Tick()`. This calls `ProcessingPhase.ProcessEvent()` which drains the `StagedProcessingQueue`. The tick mechanism ensures cooperative multitasking — projections yield control between work items, preventing any single projection from starving others.

### 2.7 Backpressure

The `CoreProjectionQueue` implements backpressure between readers and processing:

- When buffered events exceed `PendingEventsThreshold`, a `ReaderSubscriptionManagement.Pause` message is published
- The `EventReaderCoreService` pauses the reader (or forks a new paused reader if currently on the heading reader)
- When the queue drains below the threshold, `ReaderSubscriptionManagement.Resume` restarts reading
- This prevents unbounded memory growth when processing is slower than reading

### 2.8 Partition State Management

The `PartitionStateCache` provides in-memory caching of partition states with locking:

- **Locking**: States are locked from the first event processed in a partition until the corresponding checkpoint completes. This prevents eviction of uncommitted state.
- **Unlock**: `CheckpointCompleted` triggers `PartitionStateCache.Unlock(tag)`, allowing eviction of states older than the checkpoint position
- **Root partition**: Projections with `RequiresRootPartition = true` maintain a root state (key `""`) that is serialized with each checkpoint and available to all partitions
- **Bi-state projections**: Support both per-partition state and shared state via `IProjectionStateHandler.LoadShared()` / `InitializeShared()`

### 2.9 Error Handling and Restart

**Failure sources:**
- **Reader failures**: `EventReaderSubscriptionMessage.Failed` → propagated to phase → `CoreProjection.SetFaulted()`
- **Handler exceptions**: Caught in `SafeProcessEventByHandler()` → `SetFaulting()` with detailed error message
- **Checkpoint write conflicts**: `WrongExpectedVersion` → `CoreProjectionProcessingMessage.RestartRequested`

**Restart sequence** (on `RestartRequested`):
1. `EnsureUnsubscribed()` — Tears down reader subscription
2. `GoToState(Initial)` — Reinitializes caches and trackers
3. `Start()` — Reloads checkpoint and resumes from last saved position

**Faulted vs FaultedStopping**: `SetFaulting()` transitions to `FaultedStopping` (waits for checkpoint) then `Faulted`. `SetFaulted()` goes directly to `Faulted`.

---

## 3. Readers and Event Retrieval

### 3.1 EventReaderCoreService

`EventReaderCoreService` (`Services/Processing/EventReaderCoreService.cs`) manages the lifecycle of all event readers for a worker thread:

- Maintains `Dictionary<Guid, IReaderSubscription>` (subscriptions)
- Maintains `Dictionary<Guid, IEventReader>` (readers)
- Maps subscriptions ↔ readers via `_subscriptionEventReaders` / `_eventReaderSubscriptions`
- Optionally runs a `HeadingEventReader` for efficient live-tail reading

**Key message handlers:**
- `ReaderSubscriptionManagement.Subscribe` — Creates a subscription + reader pair
- `ReaderSubscriptionManagement.Unsubscribe` — Disposes reader, removes subscription
- `ReaderSubscriptionManagement.Pause` / `Resume` — Pauses/resumes reading
- `ReaderSubscriptionMessage.CommittedEventDistributed` — Routes events from readers to subscriptions
- `ReaderSubscriptionMessage.EventReaderEof` — Handles end-of-stream
- `ReaderSubscriptionMessage.EventReaderNotAuthorized` — Handles auth failures

### 3.2 Reader Types

All readers implement `IEventReader` and are created by `ReaderStrategy.CreatePausedEventReader()`:

#### TransactionFileEventReader
**Source**: `Services/Processing/TransactionFile/TransactionFileEventReader.cs`
- Reads from the transaction file (all events, `$all` stream)
- Used when `fromAll()` is specified with no event type filter
- Reads by TF position (commit/prepare position)
- Creates `ClientMessage.ReadAllEventsForward` internal messages
- Batch size: 250 events per read

#### StreamEventReader
**Source**: `Services/Processing/SingleStream/StreamEventReader.cs`
- Reads from a single named stream
- Used for `fromStream('name')` or single-category `fromCategory('name')` (reads `$ce-{name}`)
- Reads by stream sequence number
- Creates `ClientMessage.ReadStreamEventsForward` internal messages
- Batch size: 111 events per read
- Handles stream deletion, not-found, and `$maxAge`/`$maxCount` trimming
- Produces `EventReaderPartitionDeleted` notifications when configured

#### MultiStreamEventReader
**Source**: `Services/Processing/MultiStream/MultiStreamEventReader.cs`
- Reads from multiple named streams simultaneously
- Used for `fromStreams(['a', 'b', 'c'])`
- Maintains per-stream buffers for event queuing
- Reads each stream independently (111 events per batch per stream)
- Ordering logic: processes events by lowest position across all streams
- Waits for EOFs on all streams before proceeding with delay to avoid tight loops

#### EventByTypeIndexEventReader
**Source**: `Services/Processing/EventByType/EventByTypeIndexEventReader.cs`
- Reads from event type index streams (`$et-{eventType}`)
- Used for `fromAll().when({EventType: ...})` with specific event type filters
- Has two modes: **IndexBased** (reads from `$et-` streams) and **TfBased** (falls back to TF when index is behind)
- Resolves link events to get original event data
- Batch size: 50 events per read
- Handles `$deleted` event type for stream deletion notifications

#### HeadingEventReader
**Source**: `Services/Processing/TransactionFile/HeadingEventReader.cs`
- Caches recent events in memory (configurable `eventCacheSize`)
- Shared across subscriptions for efficient live-tail
- Subscriptions can be "joined" to the heading reader when they catch up
- Distributes cached events to new subscriptions for fast catch-up
- Validates strict event ordering

### 3.3 ReaderStrategy — Reader Selection Logic

`ReaderStrategy.Create()` in `Services/Processing/Strategies/ReaderStrategy.cs` selects the reader type based on the projection source definition:

```
if (allStreams && specific eventTypes)     → EventByTypeIndexEventReader
if (allStreams)                            → TransactionFileEventReader
if (single stream)                         → StreamEventReader
if (single category)                       → StreamEventReader (on $ce-{category})
if (multiple streams)                      → MultiStreamEventReader
```

### 3.4 Event Filters and Position Taggers

Each reader strategy creates matching filter and tagger pairs:

| Source | EventFilter | PositionTagger |
|--------|------------|----------------|
| `fromAll()` + event types | `EventByTypeIndexEventFilter` | `EventByTypeIndexPositionTagger` |
| `fromAll()` | `TransactionFileEventFilter` | `TransactionFilePositionTagger` |
| `fromStream(s)` | `StreamEventFilter` | `StreamPositionTagger` |
| `fromCategory(c)` | `CategoryEventFilter` | `StreamPositionTagger` (on `$ce-c`) |
| `fromStreams([...])` | `MultiStreamEventFilter` | `MultiStreamPositionTagger` |

**EventFilter** decides which events pass through to the projection (by event type, stream, link resolution).

**PositionTagger** creates `CheckpointTag` values that track the reader's position for checkpointing and resumption.

**CheckpointTag modes** (determined by tagger type):
- `Position` — TF commit/prepare position (for `$all` reading)
- `Stream` — Single stream sequence number
- `MultiStream` — Per-stream sequence number dictionary
- `EventTypeIndex` — TF position + per-event-type stream sequence numbers
- `PreparePosition` — Prepare position only (for event reordering with lag)
- `Phase` — Multi-phase projection phase number

### 3.5 ReaderSubscription

`ReaderSubscription` (`Services/Processing/Subscriptions/ReaderSubscription.cs`) sits between the reader and the projection phase:

1. Receives `CommittedEventDistributed` from the reader
2. Applies event filter (`PassesSource`, `Passes`)
3. Validates content type (optional JSON validation)
4. Updates position tracker with checkpoint tags
5. Converts to `EventReaderSubscriptionMessage.CommittedEventReceived`
6. Suggests checkpoints based on thresholds (bytes, event count, time)

The `EventReorderingReaderSubscription` variant adds event reordering with a configurable processing lag for multi-stream sources.

### 3.6 Subscription Flow

```
EventReader                    EventReaderCoreService           ReaderSubscription
    │                                  │                              │
    │──CommittedEventDistributed──────►│                              │
    │                                  │──CommittedEventDistributed──►│
    │                                  │                              │──filter + tag──┐
    │                                  │                              │◄───────────────┘
    │                                  │                              │
    │                                  │     CommittedEventReceived   │
    │                                  │◄─────────────────────────────│
    │                                  │                              │
    │                     (via ReaderSubscriptionDispatcher)          │
    │                                  │                              │
    │                                  ▼                              │
    │                    EventProcessingProjection                    │
    │                    ProcessingPhase                              │
```

---

## 4. How Projections Produce New Events

### 4.1 Emission Entry Points

Projections produce events through two mechanisms:

1. **`emit()` / `linkTo()` / `linkStreamTo()` / `copyTo()`** — Called from JavaScript projection handlers, producing `EmittedEventEnvelope[]` returned from `IProjectionStateHandler.ProcessEvent()`
2. **Result updates** — When projection state changes, the `ResultWriter` emits `Result` or `ResultRemoved` events to result streams

### 4.1.1 JavaScript Runtime (Jint)

The `JintProjectionStateHandler` (`Services/Interpreted/JintProjectionStateHandler.cs`) executes user-defined JavaScript projections using the Jint engine. It registers global functions available to projection code:

```javascript
// Available in projection JavaScript:
emit(streamId, eventType, eventBody, metadata?)    // → EmittedDataEvent
linkTo(streamId, event, metadata?)                  // → EmittedDataEvent with $> type
linkStreamTo(streamId, linkedStreamId, metadata?)   // → EmittedDataEvent linking streams
copyTo(streamId, event, metadata?)                  // → EmittedDataEvent copying event
```

- **`emit()`** creates an `EmittedDataEvent` with the provided data and adds it to the `_emitted` list
- **`linkTo()`** creates an `EmittedDataEvent` with type `$>` and data `"{sequenceNumber}@{streamId}"` (the standard link format). Uses a two-phase callback: the data event's `OnCommitted` sets the link's target event number
- All emitted events are collected during `ProcessEvent()` and returned as `EmittedEventEnvelope[]`

The handler also supports `ProcessPartitionCreated()` which can emit events when a new partition is first seen.

### 4.2 Emitted Event Types

Defined in `Services/Processing/Emitting/EmittedEvents/`:

- **`EmittedDataEvent`** — A regular data event written to a target stream (from `emit()`)
- **`EmittedLinkTo`** — A `$>` link event pointing to another event (from `linkTo()`)
- **`EmittedLinkToWithRecategorization`** — A link with category rewriting (used by `$by_category`)
- **`EmittedEventEnvelope`** — Wraps an `EmittedEvent` with stream metadata

Each emitted event carries:
- Target `StreamId`
- `CausedByTag` (CheckpointTag of the source event)
- Event type, data, metadata
- Optional `SetTargetEventNumber` callback for cross-referencing

### 4.3 Emission Pipeline

```
IProjectionStateHandler.ProcessEvent()
    │
    │ returns EmittedEventEnvelope[]
    ▼
CommittedEventWorkItem.WriteOutput()
    │
    │ calls FinalizeEventProcessing()
    ▼
┌──────────────────────────────────────┐
│  ResultWriter                         │
│  (Services/Processing/Strategies/     │
│   ResultWriter.cs)                    │
│                                       │
│  WriteRunningResult() ─── if state    │
│  │   changed, emits Result events     │
│  │                                    │
│  EventsEmitted() ─── user emit()     │
│  │   calls, forwarded to checkpoint   │
│  │                                    │
│  AccountPartition() ─── registers     │
│     new partitions in catalog stream  │
└───────────────┬──────────────────────┘
                │
                │ calls IEmittedEventWriter.EventsEmitted()
                ▼
┌──────────────────────────────────────┐
│  ICoreProjectionCheckpointManager    │
│  (ProjectionCheckpoint as            │
│   IEmittedEventWriter)               │
│                                       │
│  ValidateOrderAndEmitEvents()         │
│  │                                    │
│  Groups events by target stream       │
│  Creates EmittedStream per target     │
└───────────────┬──────────────────────┘
                │
                ▼
┌──────────────────────────────────────┐
│  EmittedStream                        │
│  (per target stream instance)         │
│                                       │
│  - Validates event ordering           │
│  - Batches writes (MaxWriteBatchLength)│
│  - Manages expected version           │
│  - Handles write retries              │
│  - Tracks emitted stream names        │
└───────────────┬──────────────────────┘
                │
                │ IODispatcher.WriteEvents()
                ▼
┌──────────────────────────────────────┐
│  EmittedStreamsWriter                  │
│  (IEmittedStreamsWriter)              │
│                                       │
│  WriteEvents(streamId, expectedVersion│
│    events[], writeAs, callback)       │
│                                       │
│  → Publishes ClientMessage.WriteEvents│
│    to the internal bus                │
└──────────────────────────────────────┘
```

### 4.4 Write Concurrency Control

The `ProjectionCheckpoint` manages write concurrency:

- **`_maximumAllowedWritesInFlight`** — Configurable limit on concurrent writes (default from `ProjectionConsts.MaxAllowedWritesInFlight`)
- **Write queue IDs** — Each `EmittedStream` is assigned to a write queue via round-robin (`_emittedStreams.Count % _maximumAllowedWritesInFlight`)
- **`QueuedEmittedStreamsWriter`** — Wraps `EmittedStreamsWriter` with a queue that limits in-flight writes per queue ID
- **`AllowedWritesInFlight.Unbounded`** — Special value that disables queuing

### 4.5 ResultEventEmitter

`ResultEventEmitter` (`Services/Processing/Emitting/ResultEventEmitter.cs`) creates events for projection results:

- **Root partition** (no partition key): Emits a single `Result` or `ResultRemoved` event to `$projections-{name}-result`
- **Named partition**: Emits a `Result`/`ResultRemoved` event to `$projections-{name}-{partition}-result` AND a `$>` link from `$projections-{name}-result` to the partition result stream

### 4.6 Checkpoint and Emit Coordination

The checkpoint mechanism ensures exactly-once emission semantics:

1. **Events are emitted to `EmittedStream` instances** within a `ProjectionCheckpoint`
2. **Checkpoint is requested** when thresholds are met (bytes, events, time)
3. **`ProjectionCheckpoint.Prepare(position)`** tells all `EmittedStream` instances to flush
4. Each `EmittedStream` calls `Checkpoint()` → flushes pending writes → signals completion
5. **Once all streams complete**, `OnCheckpointCompleted()` notifies the `CheckpointManager`
6. **`CoreProjectionCheckpointWriter`** writes the checkpoint tag + state to `$projections-{name}-checkpoint`
7. On restart, the projection resumes from the last checkpoint, and `EmittedStream` uses expected versions to deduplicate any re-emitted events

**Checkpoint loading** (`CoreProjectionCheckpointReader`): Reads the last 10 events backward from the checkpoint stream, finds the first `$ProjectionCheckpoint` event, extracts the `CheckpointTag` from metadata and state data from the event body, validates epoch/version compatibility, then publishes `CheckpointLoaded`.

**Checkpoint writing** (`CoreProjectionCheckpointWriter`): Writes a `$ProjectionCheckpoint` event to the checkpoint stream with the serialized `CheckpointTag` as metadata and root partition state as data. Retries with exponential backoff (up to 12 attempts). Warns if checkpoint exceeds 8 MB.

### 4.6.1 EmittedStream Recovery Mode

When a projection restarts, `EmittedStream` enters recovery mode to ensure exactly-once semantics:

1. Reads existing events backward from the target stream
2. Compares already-committed events with pending events using `CausedByTag` and event type
3. For matching events: fires `OnCommitted` callbacks and dequeues (event already written)
4. For link events pointing to non-existent sources: skips silently (idempotent)
5. For mismatches: throws `InvalidEmittedEventSequenceException`
6. Only truly new events are written after recovery completes

Each emitted event carries metadata:
```json
{
  "$causedBy": "<source-event-guid>",
  "$correlationId": "<correlation-id>",
  "checkpoint_tag": { ... },
  "projection_version": { ... }
}
```

Write retries use exponential backoff (up to 12 attempts, max 256 seconds).

### 4.7 EmittedStreamsTracker

`EmittedStreamsTracker` (`Services/Processing/Emitting/EmittedStreamsTracker.cs`) maintains a record of all streams a projection has written to. This is stored in a tracking stream (`$projections-{name}-emittedstreams`) and is used during projection deletion to clean up emitted streams when `deleteEmittedStreams = true`.

### 4.8 System Projections — Native Emitters

The built-in system projections use native handlers (not JavaScript) that produce events via the same pipeline:

- **`$by_category`** — Reads `$streams` and emits `$>` links to `$ce-{category}` streams
- **`$by_event_type`** — Reads `$all` and emits `$>` links to `$et-{eventType}` streams
- **`$stream_by_category`** — Emits links grouping streams by category
- **`$streams`** — Emits a record for each new stream

---

## Appendix: Key File Locations

| Component | Path |
|-----------|------|
| HTTP endpoints | `Services/Http/ProjectionsController.cs` |
| gRPC endpoints | `Services/Grpc/ProjectionManagement.*.cs` |
| Management messages | `Messages/ProjectionManagementMessage.cs` |
| ProjectionManager | `Services/Management/ProjectionManager.cs` |
| ManagedProjection | `Services/Management/ManagedProjection.cs` |
| ManagedProjection states | `Services/Management/ManagedProjectionStates/` |
| ProjectionCoreService | `Services/Processing/ProjectionCoreService.cs` |
| CoreProjection | `Services/Processing/CoreProjection.cs` |
| EventReaderCoreService | `Services/Processing/EventReaderCoreService.cs` |
| ReaderStrategy | `Services/Processing/Strategies/ReaderStrategy.cs` |
| ProcessingStrategySelector | `Services/Processing/Strategies/ProcessingStrategySelector.cs` |
| Processing phases | `Services/Processing/Phases/` |
| Reader subscriptions | `Services/Processing/Subscriptions/` |
| Event readers | `Services/Processing/SingleStream/`, `MultiStream/`, `EventByType/`, `TransactionFile/` |
| Emitting pipeline | `Services/Processing/Emitting/` |
| Emitted event types | `Services/Processing/Emitting/EmittedEvents/` |
| Checkpoint management | `Services/Processing/Checkpointing/` |
| Work items | `Services/Processing/WorkItems/` |
| ResultWriter | `Services/Processing/Strategies/ResultWriter.cs` |
| StagedProcessingQueue | `Services/Processing/StagedProcessingQueue.cs` |
| ProjectionManagerNode (wiring) | `ProjectionManagerNode.cs` |

All paths relative to `src/KurrentDB.Projections.Core/`.
