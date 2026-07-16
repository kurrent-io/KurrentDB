# Reading and Subscribing via `Streams.Read` (low-level gRPC)

A single RPC — `Streams.Read` — covers every non-persistent read and subscription in KurrentDB. Shape is controlled by four fields on `ReadReq.Options`. Sections 1–5 explain what each mode does, how dispatch works, and what targets/filters are legal. Sections 6–8 cover the individual response messages and the exact request/response/error flows for **reads** and **subscriptions** separately, with `$all` and specific-stream variants treated independently. Sections 9+ are references you'll reach for when things go wrong.

---

## 1. What each mode actually does on the server

### Read

`Enumerator.ReadAllForwards` / `ReadStreamForwards` (and backwards equivalents): a loop of page requests on the bus (`ClientMessage.ReadAllEventsForward` / `ReadStreamEventsForward`). Each response fills a bounded `Channel<ReadResponse>`; on `IsEndOfStream` or the configured count, the channel closes. No server registration, no live buffer, no state beyond the in-flight request.

### Subscription

`Enumerator.AllSubscription` / `StreamSubscription` / `AllSubscriptionFiltered` / `IndexSubscription`: a state machine with **two** channels.

1. `SubscribeToLive()` — publishes `ClientMessage.SubscribeToStream`. Server starts **pushing** `StreamEventAppeared` messages into a bounded `_liveEvents` channel, stamped with monotonic sequence numbers, and returns a `LastIndexedPosition` pivot.
2. `CatchUp(checkpoint)` — pages the log from the caller's checkpoint up to the pivot using the same paging machinery as a read.
3. `GoLive()` — reads from `_liveEvents`. If the next sequence number skips (live buffer dropped events because the caller was slow), emits `FellBehind` and loops back to `CatchUp`.
4. Loops forever. Also emits `SubscriptionConfirmed`, `CaughtUp`, `FellBehind`.

### Real differences

| Aspect             | Read                              | Subscription                                          |
|--------------------|-----------------------------------|-------------------------------------------------------|
| Server state       | None beyond in-flight request     | Registered subscription + live fanout buffer          |
| Lifetime           | Short, self-terminating           | Long-lived gRPC stream                                |
| Direction          | Forwards or backwards             | Forwards only                                         |
| Filtering          | `$all` only (any direction)       | `$all` only (forwards only)                           |
| Backpressure       | Natural — server pages on demand  | Bounded live buffer; slow caller drops-then-recovers  |
| Cost per caller    | Low                               | Higher — fanout slot + live buffer                    |
| Terminates on      | `IsEndOfStream` / count reached   | Never (caller disposes)                               |

### Reliability

- **Neither has a server-side cursor.** Resumption is entirely client-side (track revision or `(commit, prepare)`).
- **Subscriptions never silently miss events.** The sequence check in `GoLive` detects live-buffer drops and re-enters catch-up. Worst case is degraded latency, not lost events.
- Both can drop on transient `Unavailable` (leader change, shutdown). Subscriptions drop more visibly because they're long-lived.

### When to use which

- **Read**: bounded queries, backwards scans, debugging, finite bulk loads, "catch up then stop".
- **Subscribe**: tailing consumers, projections, anything that keeps up with new events.

Don't subscribe for one-shot reads. You pay for a fanout slot and live buffer you never use, and you have to decide when to dispose based on `CaughtUp` — easy to leak.

---

## 2. One RPC, four dimensions

`ReadReq.Options` has four orthogonal fields:

| Field         | Values                                   | Meaning                           |
|---------------|------------------------------------------|-----------------------------------|
| Stream target | `Stream { … }` \| `All { … }`            | A specific stream, or `$all`      |
| Count/Sub     | `Count = N` \| `Subscription = new()`    | Finite read, or live subscription |
| Direction     | `ReadDirection.Forwards` \| `Backwards`  | Order of traversal                |
| Filter        | `NoFilter = new()` \| `Filter { … }`     | Unfiltered, or filtered           |

Only the combinations below are valid. Anything else throws `InvalidArgument` ("invalid combination").

| Target  | Count/Sub    | Direction | Filter    | Server-side enumerator                |
|---------|--------------|-----------|-----------|---------------------------------------|
| Stream  | Count        | Forwards  | NoFilter  | `Enumerator.ReadStreamForwards`       |
| Stream  | Count        | Backwards | NoFilter  | `Enumerator.ReadStreamBackwards`      |
| Stream  | Subscription | Forwards  | NoFilter  | `Enumerator.StreamSubscription`       |
| `$all`  | Count        | Forwards  | NoFilter  | `Enumerator.ReadAllForwards`          |
| `$all`  | Count        | Backwards | NoFilter  | `Enumerator.ReadAllBackwards`         |
| `$all`  | Count        | Forwards  | Filter    | `ReadAllForwardsFiltered` *or* `ReadIndexForwards` (if `$idx-*` prefix)  |
| `$all`  | Count        | Backwards | Filter    | `ReadAllBackwardsFiltered` *or* `ReadIndexBackwards` (if `$idx-*` prefix) |
| `$all`  | Subscription | Forwards  | NoFilter  | `Enumerator.AllSubscription`          |
| `$all`  | Subscription | Forwards  | Filter    | `AllSubscriptionFiltered` *or* `IndexSubscription` (if `$idx-*` prefix)   |

**No filtered reads or subscriptions on a specific stream. No backwards subscriptions.**

---

## 3. Targeting streams, `$all`, and secondary indexes

### Specific stream

```csharp
Options.Stream = new() {
    StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("my-stream") },
    Start = new(),                           // or End, or Revision = <n>
};
```

### `$all`

```csharp
Options.All = new() { Start = new() };       // or End, or Position = { CommitPosition, PreparePosition }
```

### Secondary indexes (`$idx-*`)

Indexes are exposed as virtual link streams under `$idx-` on `$all`. You read/subscribe them via a **filtered `$all` request** where `Filter.StreamIdentifier.Prefix` contains exactly one entry, and it starts with `$idx-`:

| Index          | Stream name                          |
|----------------|--------------------------------------|
| Default (all)  | `$idx-all`                           |
| By category    | `$idx-ce-<Category>`                 |
| By event type  | `$idx-et-<EventType>`                |
| User-defined   | `$idx-user-<IndexName>`              |
| User + field   | `$idx-user-<IndexName>:<fieldValue>` |

When those three conditions hold, dispatch branches to the **DuckDB-backed index reader/subscriber** (`ReadIndexForwards/Backwards`, `IndexSubscription`). Otherwise the request runs as a generic filtered `$all` scan. Three hard constraints to trigger the index path:

1. **Prefix only, no `Regex`.** Setting `Regex` falls through to a scan with different semantics (no index acceleration).
2. **Exactly one prefix entry.** Mixing with other prefixes throws `InvalidArgument`.
3. **Name must start with `$idx-`.**

On the index path, `Filter.Max` and `Filter.CheckpointIntervalMultiplier` are ignored — those only apply to the generic filtered scan.

`IndexesService` is only for managing user-defined indexes (create/delete). **Reads and subscriptions always go through `Streams.Read`.**

---

## 4. Filters

### What and how

| Dimension     | Values                                            |
|---------------|---------------------------------------------------|
| What to match | `StreamIdentifier` (stream name) \| `EventType`   |
| How to match  | `Prefix` (repeated, OR'd) \| `Regex` (single)     |

Both are protobuf `oneof`s — one target, one form per request. No combining event-type and stream-name filters in the same request.

### Checkpoint controls (filtered `$all` only, non-index)

- `Filter.Max` — maxSearchWindow: how many events the server scans before emitting a `Checkpoint` message.
- `Filter.CheckpointIntervalMultiplier` — emit a checkpoint every N windows.
- Use `Count = new()` inside `Filter` instead of `Max = …` to disable checkpointing entirely.

### Shapes

```csharp
Filter = new() { StreamIdentifier = new() { Prefix = { "orders-", "payments-" } }, Max = 64, CheckpointIntervalMultiplier = 1 }
Filter = new() { StreamIdentifier = new() { Regex  = "^(orders|payments)-.*"     }, Max = 64, CheckpointIntervalMultiplier = 1 }
Filter = new() { EventType        = new() { Prefix = { "Order", "Payment" }      }, Max = 64, CheckpointIntervalMultiplier = 1 }
Filter = new() { EventType        = new() { Regex  = "^(Order|Payment).*"        }, Max = 64, CheckpointIntervalMultiplier = 1 }
```

---

## 5. Common options (applicable to every request)

```csharp
UuidOption    = new() { String = new() },     // or Structured for 16-byte GUIDs
ControlOption = new() { Compatibility = 1 },  // match the client protocol level
ResolveLinks  = false,                        // true to chase link events (system $ce-, $et-, $idx-*)
```

---

## 6. Response messages — full catalogue

`ReadResp` is a protobuf `oneof` — every server message arrives in exactly one shape. Different operations emit different subsets. The table below is exhaustive; each subsection explains what the message means and how to handle it.

| `ReadResp.ContentCase`  | Emitted by                                                                 | Stream reads | `$all` reads | `$idx-*` reads | Stream sub | `$all` sub (unfiltered) | `$all` sub (filtered) | `$idx-*` sub |
|-------------------------|----------------------------------------------------------------------------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `Event`                 | Every mode                                                                 |  ✓  |  ✓  |  ✓  |  ✓  |  ✓  |  ✓  |  ✓  |
| `StreamNotFound`        | Stream reads when the stream has no events and isn't soft-deleted          |  ✓  |     |     |     |     |     |     |
| `FirstStreamPosition`   | Stream reads — **forwards only**, once before any events                   |  ✓  |     |     |     |     |     |     |
| `LastStreamPosition`    | Stream reads — forwards (after events) and backwards (before events)       |  ✓  |     |     |     |     |     |     |
| `Confirmation`          | Subscriptions — once, at subscription establishment                        |     |     |     |  ✓  |  ✓  |  ✓  |  ✓  |
| `Checkpoint`            | **Filtered `$all` subscriptions only** — not emitted by unfiltered subs, index subs, or any read |     |     |     |     |     |  ✓  |     |
| `CaughtUp`              | Subscriptions — when transitioning from catch-up to live                   |     |     |     |  ✓  |  ✓  |  ✓  |  ✓  |
| `FellBehind`            | Subscriptions — when live buffer overflowed and subscription re-enters catch-up |     |     |     |  ✓  |  ✓  |  ✓  |  ✓  |

### `Event`
What: a recorded event. Access the event record at `resp.Event.Event`; the original `(CommitPosition, PreparePosition)` lives on `resp.Event.Event.CommitPosition` / `PreparePosition`, and `StreamRevision` is the per-stream event number.
Why: this is the actual payload you're here for.
Handle: persist the position/revision *after* you've durably processed the event, so a crash-restart replays rather than skips.

### `StreamNotFound`
What: the target stream has no events (never had any, or all events were scavenged). `resp.StreamNotFound.StreamIdentifier` echoes the stream name you requested.
Why: stream reads have to distinguish "empty response because no events matched the window" from "stream doesn't exist." This is the explicit signal for the latter.
Handle: treat as a business-level "not found." **Reading does not throw** for a nonexistent stream — you get this message and then the server closes the call normally. (A soft-deleted/tombstoned stream is different: that throws `FailedPrecondition` / `stream-deleted`.)

### `FirstStreamPosition`
What: the first revision present in the stream (usually 0, unless earlier events were scavenged). Emitted once, before any events, on **forward stream reads only**.
Why: lets the caller know the true bottom of the stream without a separate query. Useful for building "first/last" pagination or for clients that care about the scavenge frontier.
Handle: usually nothing — just note it if you care about the lower bound.

### `LastStreamPosition`
What: the last revision present in the stream. Emitted once per stream read — *after* events on forwards, *before* events on backwards.
Why: tells the caller the current tip. On backwards reads, it's emitted first so you know the high-water mark before you start consuming events in descending order.
Handle: useful for pagination decisions ("am I at the end?") and for setting the next resume point.

### `Confirmation`
What: subscription established. `resp.Confirmation.SubscriptionId` is a server-assigned ID for logging.
Why: signals the handoff from "server is setting up fanout + pivot" to "events will now flow." Historically also the place where some clients started accepting events; in practice the server just starts sending after this.
Handle: log the ID for correlation. You don't need to do anything functional with it — the event stream will follow.

### `Checkpoint`
What: "I've scanned up to this position on `$all` and nothing matched your filter; it's safe to advance your stored checkpoint." Contains `(CommitPosition, PreparePosition)`. Emitted only by **filtered `$all` subscriptions**, controlled by `Filter.Max` and `Filter.CheckpointIntervalMultiplier`.
Why: under a narrow filter on a busy log, match events can be minutes or hours apart. Without checkpoints, a consumer that restarts would re-scan enormous portions of `$all` to confirm no matches. Checkpoints let you advance your resume position even when nothing matched.
Handle: persist the position the same way you would for an `Event` — this is the *progress signal*. Do not treat it as data; it has no event payload.

### `CaughtUp`
What: the subscription has consumed everything that existed up to its pivot and is now serving live events from the push channel. Contains a timestamp and checkpoint position.
Why: allows consumers that need different behaviour in "catch-up" vs "live" modes (e.g. batch on catch-up, respond per-event when live) to detect the transition.
Handle: optional. Many consumers ignore it. If you care: flip your processing mode, update a health metric, or start emitting "live" telemetry.

### `FellBehind`
What: the live buffer detected a sequence gap (events were dropped because the caller was too slow to read), and the subscription is falling back to catch-up. Contains timestamp and checkpoint.
Why: an honest signal that you're not keeping up. The subscription will still deliver every event correctly (via catch-up re-reading the log), but with degraded latency.
Handle: alert or log at warning level if persistent — it means your consumer throughput is below the write rate. Adding parallelism or reducing per-event work is the real fix; raising buffer sizes just delays the same problem.

---

## 7. Reads

### 7.1 Read a specific stream

**Request**

```csharp
var req = new ReadReq {
    Options = new() {
        Stream = new() {
            StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("my-stream") },
            Start = new(),                                      // or End, or Revision = <n>
        },
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,  // or Backwards
        Count = 1000,
        ResolveLinks = false,
        NoFilter = new(),                                       // filters not allowed here
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Expected response frames** (forwards): `FirstStreamPosition` → zero or more `Event` → `LastStreamPosition`, or `StreamNotFound` alone.
**Expected response frames** (backwards): `LastStreamPosition` → zero or more `Event` (in descending order), or `StreamNotFound` alone.

**Consume**

```csharp
using var call = streams.Read(req, cancellationToken: ct);
try {
    await foreach (var resp in call.ResponseStream.ReadAllAsync(ct)) {
        switch (resp.ContentCase) {
            case ReadResp.ContentOneofCase.FirstStreamPosition:
                // forward reads only; usually informational
                break;
            case ReadResp.ContentOneofCase.LastStreamPosition:
                // use to decide whether to paginate further
                break;
            case ReadResp.ContentOneofCase.Event:
                await Process(resp.Event.Event, ct);
                // persist resp.Event.Event.StreamRevision for resume
                break;
            case ReadResp.ContentOneofCase.StreamNotFound:
                // business "not found" — stream truly has no events
                return;
        }
    }
} catch (RpcException ex) {
    switch (ex.StatusCode, ex.Trailers.GetValue("exception")) {
        case (StatusCode.FailedPrecondition, "stream-deleted"):
            // soft-deleted / tombstoned — different from StreamNotFound
            throw;
        case (StatusCode.PermissionDenied, "access-denied"):
            throw new UnauthorizedAccessException();
        default: throw;
    }
}
```

**Mode-specific errors**

| Condition                              | `StatusCode`         | `exception` trailer | Extra trailers |
|----------------------------------------|----------------------|---------------------|----------------|
| Stream tombstoned                      | `FailedPrecondition` | `stream-deleted`    | `stream-name`  |
| Invalid `Revision` (past the end)      | `InvalidArgument`    | —                   | —              |

Empty stream is **not** an error — you get `StreamNotFound` as a response frame and the call completes normally.

---

### 7.2 Read `$all`

**Request** (unfiltered)

```csharp
var req = new ReadReq {
    Options = new() {
        All = new() { Start = new() },                          // or End, or Position = { C, P }
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
        Count = 1000,
        ResolveLinks = false,
        NoFilter = new(),
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Request** (filtered — any `Filter` shape from §4)

Same as above but replace `NoFilter = new()` with `Filter = …`. If the filter is `StreamIdentifier.Prefix = { "$idx-*" }` (single entry), dispatch branches to the index reader — see §7.3.

**Expected response frames**: `Event` only. `$all` reads emit **no position or stream-bounds frames** — the call simply closes when finished.

**Consume**

```csharp
using var call = streams.Read(req, cancellationToken: ct);
await foreach (var resp in call.ResponseStream.ReadAllAsync(ct)) {
    if (resp.ContentCase == ReadResp.ContentOneofCase.Event) {
        var e = resp.Event.Event;
        await Process(e, ct);
        // persist (e.CommitPosition, e.PreparePosition) for resume
    }
}
```

**Mode-specific errors**

| Condition                                 | `StatusCode`       | `exception` trailer | Notes                                        |
|-------------------------------------------|--------------------|---------------------|----------------------------------------------|
| `Position` doesn't exist on the log       | `InvalidArgument`  | —                   | Commonly happens when resuming from a position that was scavenged |
| Invalid combination (e.g. stream+filter)  | `InvalidArgument`  | —                   | Reason in `Status.Detail`                    |

---

### 7.3 Read a secondary index (`$idx-*`)

**Request**

```csharp
var req = new ReadReq {
    Options = new() {
        All = new() { Start = new() },                          // or End, or Position
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
        Count = ulong.MaxValue,
        Filter = new() {
            StreamIdentifier = new() { Prefix = { "$idx-et-OrderCreated" } },   // exactly one, no Regex
        },
        ResolveLinks = false,
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Expected response frames**: `Event` only. Index reads emit no position frames. `resp.Event.Event.StreamIdentifier` will be the **original** stream, not `$idx-*`.

**Mode-specific errors**

| Condition                                                    | `StatusCode`      | `exception` trailer | Extra trailers |
|--------------------------------------------------------------|-------------------|---------------------|----------------|
| `$idx-*` name doesn't resolve (typo, not yet created, e.g. `$idx-woops`) | `NotFound`        | `index-not-found`   | `index-name`   |
| Multiple prefixes, with at least one `$idx-*`                | `InvalidArgument` | —                   | Detail: *"Index reads only work with one index name and cannot be combined with stream prefixes or other indexes"* |

```csharp
catch (RpcException ex)
    when (ex.StatusCode == StatusCode.NotFound
       && ex.Trailers.GetValue("exception") == "index-not-found") {
    var name = ex.Trailers.GetValue("index-name");
    // index doesn't exist — stop, validate, or create it
}
```

---

## 8. Subscriptions

### 8.1 Subscribe to a specific stream

**Request**

```csharp
var req = new ReadReq {
    Options = new() {
        Stream = new() {
            StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("my-stream") },
            Start = new(),                                      // or Revision = <last processed + 1>
        },
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
        Subscription = new(),
        ResolveLinks = false,
        NoFilter = new(),                                       // filters not allowed
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Expected response frames**: `Confirmation` → mix of `Event` / `CaughtUp` / `FellBehind`. No `Checkpoint`, no `StreamNotFound`, no position frames.

**Nonexistent stream behaviour**: the subscription simply waits — the server does **not** emit `StreamNotFound` on subscriptions. When events appear, they'll be delivered.

**Consume**

```csharp
using var call = streams.Read(req, cancellationToken: ct);
try {
    await foreach (var resp in call.ResponseStream.ReadAllAsync(ct)) {
        switch (resp.ContentCase) {
            case ReadResp.ContentOneofCase.Confirmation:
                // subscription live — resp.Confirmation.SubscriptionId is the server-side ID
                break;
            case ReadResp.ContentOneofCase.Event:
                var e = resp.Event.Event;
                await Process(e, ct);
                // persist e.StreamRevision
                break;
            case ReadResp.ContentOneofCase.CaughtUp:
                // optional: switch from catch-up mode to live mode in your consumer
                break;
            case ReadResp.ContentOneofCase.FellBehind:
                // live buffer overflowed — log/alert if persistent
                break;
        }
    }
} catch (RpcException ex)
    when (ex.StatusCode is StatusCode.Unavailable or StatusCode.Cancelled) {
    // transient: re-subscribe from the last persisted revision
}
```

**Mode-specific errors**

| Condition                              | `StatusCode`         | `exception` trailer |
|----------------------------------------|----------------------|---------------------|
| Stream tombstoned                      | `FailedPrecondition` | `stream-deleted`    |
| Leader election / node drop            | `Unavailable`        | —                   |

---

### 8.2 Subscribe to `$all` (unfiltered)

**Request**

```csharp
var req = new ReadReq {
    Options = new() {
        All = new() { Start = new() },                          // or Position = { C, P } to resume
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
        Subscription = new(),
        ResolveLinks = false,
        NoFilter = new(),
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Expected response frames**: `Confirmation` → mix of `Event` / `CaughtUp` / `FellBehind`. **No `Checkpoint`** — checkpoints only exist on *filtered* `$all` subscriptions.

**Consume**: same shape as §8.1 but persist `(e.CommitPosition, e.PreparePosition)` instead of `e.StreamRevision`.

---

### 8.3 Subscribe to `$all` (filtered)

**Request**

```csharp
var req = new ReadReq {
    Options = new() {
        All = new() { Start = new() },
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
        Subscription = new(),
        Filter = new() {
            EventType = new() { Prefix = { "Order", "Payment" } },
            Max = 1000,                                          // maxSearchWindow
            CheckpointIntervalMultiplier = 1,
        },
        ResolveLinks = false,
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Expected response frames**: `Confirmation` → mix of `Event` / `Checkpoint` / `CaughtUp` / `FellBehind`. **`Checkpoint` is the critical addition** — persist it so you advance during sparse-match stretches.

**Consume**

```csharp
using var call = streams.Read(req, cancellationToken: ct);
await foreach (var resp in call.ResponseStream.ReadAllAsync(ct)) {
    switch (resp.ContentCase) {
        case ReadResp.ContentOneofCase.Confirmation:
            break;
        case ReadResp.ContentOneofCase.Event:
            var e = resp.Event.Event;
            await Process(e, ct);
            await PersistPosition(e.CommitPosition, e.PreparePosition);
            break;
        case ReadResp.ContentOneofCase.Checkpoint:
            // progress frame — no event, just "safe to advance to this position"
            await PersistPosition(resp.Checkpoint.CommitPosition, resp.Checkpoint.PreparePosition);
            break;
        case ReadResp.ContentOneofCase.CaughtUp:
        case ReadResp.ContentOneofCase.FellBehind:
            break;
    }
}
```

**Checkpoint sizing**: small `Max` means frequent progress updates and chatty output; large `Max` means less progress feedback but cheaper bookkeeping. Defaults of `Max = 1000, CheckpointIntervalMultiplier = 1` are usually fine.

---

### 8.4 Subscribe to a secondary index (`$idx-*`)

**Request**

```csharp
var req = new ReadReq {
    Options = new() {
        All = new() { Start = new() },
        ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
        Subscription = new(),
        Filter = new() {
            StreamIdentifier = new() { Prefix = { "$idx-et-OrderCreated" } },   // exactly one, no Regex
        },
        ResolveLinks = false,
        UuidOption = new() { String = new() },
        ControlOption = new() { Compatibility = 1 },
    }
};
```

**Expected response frames**: `Confirmation` → mix of `Event` / `CaughtUp` / `FellBehind`. **No `Checkpoint`** — even though it's dispatched via the filter oneof, the index subscription path doesn't emit checkpoint frames. Resume state is the event position alone.

**Mode-specific errors**: same as §7.3 (`index-not-found` on unresolved `$idx-*` name; `InvalidArgument` on multiple prefixes).

---

## 9. Resumability (no server-side cursor)

KurrentDB holds no per-client cursor. Resumption is entirely client-side:

- **Stream**: persist `evt.Event.StreamRevision`; resume with `Stream.Revision = last + 1`.
- **`$all`**: persist `(evt.Event.CommitPosition, evt.Event.PreparePosition)`; resume with `All.Position = { CommitPosition, PreparePosition }`.
- **Filtered `$all` subscriptions**: persist `Checkpoint` frames as well as events. Without this, a long sparse stretch means no state advances, and a restart will re-scan from the last event position.

Persist *after* durable processing, not before, so a crash-restart re-processes rather than skips.

---

## 10. Full error catalogue

Dispatch on `(ex.StatusCode, ex.Trailers.GetValue("exception"))`. Never switch on `Status.Detail`.

| Condition                                                                                    | `StatusCode`         | `exception` trailer | Extra trailers                                |
|----------------------------------------------------------------------------------------------|----------------------|---------------------|-----------------------------------------------|
| `$idx-*` name doesn't resolve (§7.3 / §8.4)                                                  | `NotFound`           | `index-not-found`   | `index-name`                                  |
| Stream tombstoned / soft-deleted                                                             | `FailedPrecondition` | `stream-deleted`    | `stream-name`                                 |
| Caller lacks permission                                                                      | `PermissionDenied`   | `access-denied`     | —                                             |
| Invalid option combination (stream+filter, multiple `$idx-*` prefixes, bad `Start` position) | `InvalidArgument`    | *(none)*            | — (reason in `Status.Detail`)                 |
| Node not ready / busy / no leader known                                                      | `Unavailable`        | —                   | —                                             |
| Follower with known leader (`requiresLeader=true`)                                           | redirect             | —                   | `leader-endpoint-host`, `leader-endpoint-port`|
| Read deadline exceeded                                                                       | `DeadlineExceeded`   | —                   | —                                             |
| Caller cancelled                                                                             | `Cancelled` (or `OperationCanceledException`) | —  | —                                             |
| Unexpected server state                                                                      | `Unknown`            | —                   | —                                             |

---

## 11. Best practices

- **Match the operation to the intent.** Read for bounded work, subscribe for ongoing tailing. Don't subscribe "just to read once".
- **Persist positions on the consumer side, always.** Server has no memory.
- **On filtered `$all` subscriptions, persist `Checkpoint` frames.** Otherwise sparse filters cause you to re-scan on restart.
- **Handle `CaughtUp`/`FellBehind` as status, not data.** Informational. Ignore unless you need the distinction.
- **Prefer prefix filters over regex.** Prefix lists run as direct string comparisons; regex compiles a matcher per request and runs per event.
- **Route at write time when possible.** If you'll always want "OrderCreated events," write them to `orders-<id>` or use a category/event-type/user index — don't scan `$all` repeatedly.
- **Size `Max` sensibly for sparse filters.** 1000 is a reasonable default.
- **On `$idx-*`, leave `Max`/`CheckpointIntervalMultiplier` at defaults.** They're ignored; cluttering the request obscures intent.
- **Expect index latency.** Writes are indexed asynchronously; read-your-write on `$idx-*` needs a brief retry. Tests in this repo use `[Retry(50)]`.
- **Propagate `CancellationToken` everywhere.** Disposing a subscription without cancelling leaves resources in flight longer than necessary.

---

## 12. Troubleshooting / common gotchas

- **"My regex filter on `$idx-et-MyEvent` returns real stream events, not index entries."** Index dispatch is prefix-only. `Regex` falls through to a regular `$all` scan that matches against user stream names (not `$idx-*`). Use `Prefix = { "$idx-et-MyEvent" }`.
- **"I sent two prefixes, one starts with `$idx-`, and got InvalidArgument."** The index reader accepts exactly one prefix. Split into two requests or drop the non-index prefix.
- **"Filter works on `$all` but InvalidArgument on my stream."** Stream reads/subscriptions disallow filters. Target `$all` with an appropriate filter, or read the stream unfiltered.
- **"No backwards subscription."** Not supported. For "latest first" live, subscribe forwards and reverse client-side, or read backwards with `Count` for a finite snapshot.
- **"StreamNotFound never appears on my subscription."** It's a read-only response type. Subscriptions for a nonexistent stream just wait for events to appear.
- **"I'm getting StreamNotFound but expected FailedPrecondition."** `StreamNotFound` means the stream is empty (never written, or scavenged). `FailedPrecondition` with `stream-deleted` means the stream was explicitly tombstoned. Different states, different responses.
- **"Subscription silently drops events under load."** It doesn't — when the live buffer overflows, the sequence check detects the gap and transitions back to catch-up. You'll see `FellBehind`, then catch-up events, then `CaughtUp` again. If this happens repeatedly, the consumer is too slow.
- **"Index read immediately after write returns nothing."** Indexing is asynchronous to the append. Retry with a short delay.
- **"`Max = 64` but nothing changes on my index read."** On the index path it's ignored. On the generic filtered-`$all` path it controls checkpoint emission, not result size.
- **"My filtered subscription restarts from the beginning even though I was at event N."** You probably persisted only event positions, not `Checkpoint` frames. Add the `Checkpoint` branch to your handler.
- **"`requiresLeader=true` fails on a follower."** Expected. Inspect `leader-endpoint-host` / `leader-endpoint-port` trailers and retry against the leader.
- **"Subscription survives a leader election?"** No — expect a drop with `Unavailable`. Client must re-subscribe from the last persisted position.
- **"`ResolveLinks = true` makes me see the original stream names on index reads."** Yes — that's the whole point. Leave it `false` and you'll see the `$idx-*` link records instead (rarely what you want).

---

## 13. Source of truth (for verification)

- Dispatch table and valid combinations: `src/KurrentDB.Core/Services/Transport/Grpc/Streams.Read.cs` (the big `switch` on `(streamOptionsCase, countOptionsCase, readDirection, filterOptionsCase)`).
- Index dispatch, filter conversion, error mapping: same file (`GetFilterOrIndexEnumerator`, `ConvertToEventFilter`, `ConvertReadResponseException`).
- Per-mode enumerators: `src/KurrentDB.Core/Services/Transport/Enumerators/Enumerator.*.cs`.
- Response type hierarchy: `src/KurrentDB.Core/Services/Transport/Enumerators/ReadResponse.cs`.
- Index stream name constants: `src/KurrentDB.Core/Services/SystemNames.cs`.
- Trailer keys and error codes: `src/KurrentDB.Core/Services/Transport/Grpc/Constants.cs`.
- `RpcException` factories: `src/KurrentDB.Core/Services/Transport/Grpc/RpcExceptions.cs`.
