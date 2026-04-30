---
order: 5
---

# Projections engine V2

<Badge text="Experimental" type="warning" vertical="middle"/>

KurrentDB ships a next-generation projection engine ("V2") alongside the original engine ("V1"). V2 is selected
per-projection at creation time via the `engineversion` option. V1 remains the default; V2 is opt-in.

::: warning
V2 is a distinct execution engine, **not** a drop-in upgrade of V1. It persists checkpoints and per-partition
state in a new, incompatible format, and it deliberately omits some V1 features. Read the [limitations](#limitations)
section before choosing V2 for an existing workload.
:::

## When to choose V2

The V2 engine is designed for projections whose bottleneck is handler throughput on the hot path:

- Partition processing runs in parallel. V1 processes all events on a single
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

Consult with KurrentDB clients documentation for the projection options.

### Supported source selectors

V2 supports the same read selectors as V1 via a single filtered `$all` subscription under the hood:

- `fromAll()`
- `fromStream(name)`
- `fromStreams(["a", "b", ...])`
- `fromCategory(name)`

Event type filtering, custom partitioning (`partitionBy`), per-stream partitioning (`foreachStream`),
and `$deleted` notifications all work on V2. Bi-state projections (`$initShared` / `fromStreams`-style
shared state) are **not** yet supported — see [limitations](#bi-state-projections-are-not-supported).

## Limitations

### Result streams are not emitted

V1's `outputState()` produces `Result` events on `$projections-{name}-result` (and per-partition result
streams with link-tos) so consumers can **subscribe** to state updates.

V2 does not emit result events. State is written only to `$projections-{name}[-{partition}]-state`
at checkpoint time. Consumers must either:

- Poll state via the management API (`GET /projection/{name}/state[?partition={key}]` / gRPC
  `Projections.Result`), or
- Read / subscribe to the `…-state` stream directly, accepting that updates are visible only at checkpoint
  cadence.

Live result streaming parity is planned for a future release. Projections that rely on live result
streams should stay on V1 until then.

### Bi-state projections are not yet supported

Projections that declare `$initShared` (bi-state projections — handlers operating on a `[partitionState, sharedState]`
pair, e.g. `function (s, e) { ... }` where `s` is `[s[0], s[1]]`) are not supported by V2.

The shared state slot is not restored on engine restart. After a node restart or projection re-enable, the
shared state would be silently re-initialized from `$initShared` instead of being read back from
`$projections-{name}-state`, producing incorrect results without any error. Until shared-state restore is
implemented, projections that rely on `$initShared` must stay on V1.

### `trackEmittedStreams` is rejected

Creating a V2 projection with `trackemittedstreams=true` is rejected with an error:

```
Tracking emitted streams is not supported with engine version 2.
```

V2 does not maintain an emitted-streams catalog. If you need projection deletion to also tombstone the
streams it wrote to, stay on V1.

### No migration from V1 to V2

V1 and V2 write to the same set of stream names (`$projections-{name}-checkpoint`, `$projections-{name}-state`,
`$projections-{name}-{partition}-state`), but the event types and payloads on those streams are not
interchangeable. A V2 engine will not read V1 events and vice versa.

| Aspect                          | V1                                                                           | V2                                                     |
|---------------------------------|------------------------------------------------------------------------------|--------------------------------------------------------|
| Checkpoint event type           | `$ProjectionCheckpoint`                                                      | `$ProjectionCheckpoint.V2`                             |
| Checkpoint payload              | Serialised `CheckpointTag` (phase + stream positions + event numbers)        | `{"commitPosition":…,"preparePosition":…}`             |
| State event type                | `$Checkpoint` (per-partition checkpoint stream + per-partition state stream) | `$ProjectionState.V2` (root + partition state streams) |
| Per-partition checkpoint stream | `$projections-{name}-{partition}-checkpoint`                                 | Not written — single root checkpoint only              |

The engine version is pinned at Create and cannot be changed via `Update` / `UpdateQuery`. To move an
existing projection from V1 to V2 today:

1. Stop the V1 projection.
2. Delete the V1 projection.
3. Create a new projection with the same query and `engineversion=2`.
4. Reprocess from `TFPos(0, 0)` (V2 starts from the beginning on a missing / unreadable checkpoint).

There is no in-place checkpoint conversion and no partition-state carry-over. Consumers reading V1 result
streams also need to switch to the V2 state-polling model (see [outputState](#result-streams-are-not-emitted)).

An assisted migration tool is planned for a later release and will not ship with the initial V2 engine.

## Checkpoint and state streams

For reference, V2 writes to these streams (stream names are shared with V1; only the event types and
payloads on them are V2-specific):

- `$projections-{name}-checkpoint` — a single `$ProjectionCheckpoint.V2` event per checkpoint, payload is the
  log position JSON `{"commitPosition":…,"preparePosition":…}`.
- `$projections-{name}-state` — root/shared state, one `$ProjectionState.V2` event per checkpoint when state
  changed during the window.
- `$projections-{name}-{partition}-state` — per-partition state, same event type, same cadence.
- Streams written to via `emit()` / `linkTo()` inside handler code — unchanged from V1.

All writes that belong to one checkpoint land in a single multi-stream write, so a successful checkpoint
is observable as an atomic unit.

## Operational notes

- V2 respects the same checkpoint tuning knobs as V1: `CheckpointAfterMs`, `CheckpointHandledThreshold`,
  `CheckpointUnhandledBytesThreshold`.
- V2 defaults to 4 parallel partition slots. These do not occupy V1 projection worker threads.
- Per-partition state is held in a bounded in-memory cache sized by `MaxPartitionStateCacheSize`
  (default `100000`). When the cache is full, partitions are evicted; on the next event for an evicted
  partition V2 reloads its state from `$projections-{name}-{partition}-state` before invoking the handler.
  For very high-cardinality partition spaces, raise this value if the hot working set exceeds the default —
  otherwise expect additional state-stream reads.
- `GET /projection/{name}/state` and `GET /projection/{name}/result` both return the same per-partition
  state on V2 — the V1 distinction between "state" and "result" does not exist in V2.
- V2 runs through the same `ProjectionManager` state machine as V1, so Enable / Disable / Reset / Abort
  behave the same way from the operator's perspective. Reset on V2 clears the V2 checkpoint and restarts
  from `TFPos(0, 0)`.
