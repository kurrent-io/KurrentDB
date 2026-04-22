---
order: 5
---

# Projections Engine V2

KurrentDB ships a next-generation projection engine ("V2") alongside the original engine ("V1"). V2 is selected
per-projection at creation time via the `engineversion` option. V1 remains the default; V2 is opt-in.

::: warning
V2 is a distinct execution engine, **not** a drop-in upgrade of V1. It persists checkpoints and per-partition
state in a new, incompatible format, and it deliberately omits some V1 features. Read the [limitations](#limitations)
section before choosing V2 for an existing workload.
:::

## When to choose V2

V2 is designed for projections whose bottleneck is handler throughput on the hot path:

- Partition processing runs in parallel across multiple worker slots. V1 processes all events on a single
  pipeline; V2 hashes events by partition key and dispatches them to independent processors.
- Checkpoints are written atomically in a single multi-stream write (Chandy–Lamport style snapshot), so
  per-partition state, emitted events, and the checkpoint position are consistent with each other.

Choose V1 if you need any of the features listed under [limitations](#limitations), especially
`outputState()` / result streams or `trackEmittedStreams`.

## Creating a V2 projection

Pass `engineversion=2` at creation. The option is only honoured on **Create**; existing projections keep
the engine version they were created with.

### HTTP

```bash
curl -i -d@projection.json \
  "http://localhost:2113/projections/continuous?name=my_projection&type=js&enabled=true&emit=true&engineversion=2" \
  -u admin:changeit
```

### gRPC

Set `CreateReq.Options.EngineVersion = 2` on the create request. Passing `0` or `1` selects V1 (the default).

### Supported source selectors

V2 supports the same read selectors as V1 via a single filtered `$all` subscription under the hood:

- `fromAll()`
- `fromStream(name)`
- `fromStreams(["a", "b", ...])`
- `fromCategory(name)`

Event type filtering, custom partitioning (`partitionBy`), per-stream partitioning (`foreachStream`), bi-state
projections (`$initShared`), and `$deleted` notifications all work on V2.

## Limitations

### `outputState()` / result streams are not emitted

V1's `outputState()` produces `Result` events on `$projections-{name}-result` (and per-partition result
streams with link-tos) so consumers can **subscribe** to state updates.

V2 does not emit result events. State is written only to `$projections-{name}[-{partition}]-state`
at checkpoint time. Consumers must either:

- Poll state via the management API (`GET /projection/{name}/state[?partition={key}]` / gRPC
  `Projections.Result`), or
- Read / subscribe to the `…-state` stream directly, accepting that updates are visible only at checkpoint
  cadence and use `ExpectedVersion.Any`.

This is tracked for a future release (see DB-2039). Projections that rely on live result streams should
stay on V1 until parity is shipped.

### `trackEmittedStreams` is rejected

Creating a V2 projection with `trackemittedstreams=true` is rejected with an error:

```
Tracking emitted streams is not supported with engine version 2.
```

V2 does not maintain an emitted-streams catalog. If you need projection deletion to also tombstone the
streams it wrote to, stay on V1.

### No migration from V1 to V2

V1 and V2 write to the same checkpoint stream (`$projections-{name}-checkpoint`) but with incompatible
event types and payloads:

| Aspect                | V1                                          | V2                                      |
|-----------------------|---------------------------------------------|-----------------------------------------|
| Checkpoint event type | `$ProjectionCheckpoint`                     | `$ProjectionCheckpoint.V2`              |
| Checkpoint payload    | Serialised `CheckpointTag` (phase + stream positions + event numbers) | `{"commitPosition":…,"preparePosition":…}` |
| State event type      | `$Checkpoint` (per-partition checkpoint stream) | `$ProjectionState.V2` (per-partition state stream) |
| Per-partition checkpoints | Written to `…-{partition}-checkpoint` | Not used — state stream is authoritative |

The engine version is pinned at Create and cannot be changed via `Update` / `UpdateQuery`. To move an
existing projection from V1 to V2 today:

1. Stop the V1 projection.
2. Delete the V1 projection.
3. Create a new projection with the same query and `engineversion=2`.
4. Reprocess from `TFPos(0, 0)` (V2 starts from the beginning on a missing / unreadable checkpoint).

There is no in-place checkpoint conversion and no partition-state carry-over. Consumers reading V1 result
streams also need to switch to the V2 state-polling model (see [outputState](#outputstate-result-streams-are-not-emitted)).

An assisted migration tool is tracked for a later release (DB-2041); it will not ship with the initial V2
release.

### Partition state cache is in memory

V2 keeps per-partition state in memory and persists it to the state stream at checkpoint time. The engine
also maintains a shared partition-state dictionary for management-API lookups. Before general availability,
both caches are **bounded** via configuration (tracked in DB-2040). If you are evaluating the initial V2
engine on a preview build with unbounded caches, avoid high-cardinality partition keys (large
`foreachStream` / custom-partition spaces) — memory grows with the number of distinct partition keys
observed in a run.

## Checkpoint and state streams

For reference, V2 writes to these streams:

- `$projections-{name}-checkpoint` — a single `$ProjectionCheckpoint.V2` event per checkpoint, payload is the
  log position JSON.
- `$projections-{name}-state` — root/shared state, one `$ProjectionState.V2` event per checkpoint when state
  changed during the window.
- `$projections-{name}-{partition}-state` — per-partition state, same event type, same cadence.
- Streams written to via `emit()` / `linkTo()` inside handler code — unchanged from V1.

All writes that belong to one checkpoint land in a single multi-stream write, so a successful checkpoint
is observable as an atomic unit.

## Operational notes

- V2 respects the same checkpoint tuning knobs as V1: `CheckpointAfterMs`, `CheckpointHandledThreshold`,
  `CheckpointUnhandledBytesThreshold`.
- V2 defaults to 4 parallel partition slots. Bi-state projections (those declaring `$initShared`) are forced
  to a single slot because shared state requires sequential processing.
- `GET /projection/{name}/state` and `GET /projection/{name}/result` both return the same per-partition
  state on V2 — the V1 distinction between "state" and "result" does not exist in V2.
- V2 runs through the same `ProjectionManager` state machine as V1, so Enable / Disable / Reset / Abort
  behave the same way from the operator's perspective. Reset on V2 clears the V2 checkpoint and restarts
  from `TFPos(0, 0)`.
