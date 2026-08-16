# Projection progress tracking

`ProjectionProgressTracker` measures how far a projection is behind its source and the time that
each commit uses. It sends three metrics through one `Meter`:

| Metric                             | Instrument       | Unit    | Meaning                                           |
|------------------------------------|------------------|---------|---------------------------------------------------|
| `{service}.{scope}.gap`            | Observable gauge | `bytes` | Head position minus the last processed position   |
| `{service}.{scope}.lag`            | Observable gauge | `s`     | Head timestamp minus the last processed timestamp |
| `{service}.{scope}.commit.seconds` | Histogram        | `s`     | The duration of one commit                        |

Each sample has one tag: `{TagKey}={Name}`.

The gauges are pull-based. The metrics scrape reads them. They give no samples until the head
and the processed side each have a value.

## Make a tracker

```csharp
var tracker = new ProjectionProgressTracker(
    serviceName: "kurrentdb",
    options: new() {
        Scope   = "kontext.projections",
        TagKey  = "projection",
        Name    = "memories",
        GetHead = () => head.Value,
    },
    meter,
    TimeProvider.System,
    logger);
```

## Supply the head

The head is the latest available mark of the source. One source has one head. Many trackers can
read it.

Keep the head in one `Atomic<ProgressMark>` cell near the subscription. Give each tracker a
delegate that reads the cell.

```csharp
Atomic<ProgressMark> _head;

// On each record that the source appends:
_head.Value = new ProgressMark(record.LogPosition, record.Timestamp);
```

## Seed at startup

Read the checkpoint. Then call `RecordProcessed` before the first scrape. Without the seed, the
gauges give no samples until the projection processes its first record.

```csharp
tracker.RecordProcessed(new ProgressMark(checkpoint.LogPosition, checkpoint.Timestamp));
```

## Record progress and commits

```csharp
// After the projection processes a record:
tracker.RecordProcessed(new ProgressMark(record.LogPosition, record.Timestamp));

// Around each batch flush:
using (tracker.StartCommit())
    await appender.FlushAsync(ct);
```

`StartCommit` returns a scope that you dispose. `Dispose` records the time in the histogram and
writes one debug log line.

## Threads and clocks

Call `RecordProcessed` from the processing thread. The scrape thread reads the mark through a
DotNext `Atomic<ProgressMark>`. A reader always gets a complete pair.

The lag gauge subtracts record timestamps. The head and the processed side must supply timestamps
from the same clock.
