# KurrentDB.Projections.Management

Top-level orchestration layer for the projections subsystem.

## Responsibilities

- **Lifecycle management** — Creating, enabling, disabling, stopping, deleting, and resetting projections via the `ManagedProjection` state machine.
- **APIs** — gRPC and HTTP endpoints for all projection operations (create, update, delete, statistics, state/result queries).
- **Subsystem coordination** — `ProjectionsSubsystem` manages startup/shutdown and leader election awareness (projections only run on the leader node).
- **Projection registry** — `ProjectionManager` maintains the registry of all active projections and dispatches commands to them.
- **Worker distribution** — `ProjectionCoreCoordinator` manages multiple projection worker threads; `ProjectionWorkerNode` sets up individual workers.
- **Engine selection** — `ProcessingStrategySelector` chooses between V1 and V2 engines per projection.
- **Prelude** — Serves the JavaScript projection API files (`Prelude/`) over HTTP for the web editor.

## Key Classes

| Class | Purpose |
|-------|---------|
| `ProjectionsSubsystem` | Plugin/subsystem entry point, lifecycle, metrics |
| `ProjectionManager` | Central request dispatcher and projection registry |
| `ManagedProjection` | Wraps an individual projection with a state machine |
| `ProjectionCoreCoordinator` | Coordinates multiple projection core workers |
| `ProjectionCoreService` | Per-worker projection processing service |
| `ProcessingStrategySelector` | Selects V1 or V2 engine per projection |
| `ProjectionManagement` (gRPC) | gRPC service implementation |
| `ProjectionsController` (HTTP) | HTTP controller for REST/legacy clients |

## Dependencies

```
Management ──→ V1 ──→ Shared
    │                   ↑
    ├──→ V2 ────────────┘
    └──→ JavaScript
```

Management directly references V1, V2, and JavaScript. V1, V2, and JavaScript have no cross-dependencies — each depends only on Shared.
