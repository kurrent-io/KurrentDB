---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v26.1.

## [26.1.2](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.1.2)

5 August 2026

### Connectors: Fixed sink resource cleanup when a connector stops unexpectedly

Connectors now release their sink resources on every stop path, including a processing error or a leadership change. Previously, cleanup only ran when a connector was stopped through the API.

### Projections: Fixed projections stalling after a failed checkpoint write (PR [#5699](https://github.com/kurrent-io/KurrentDB/pull/5699))

A projection whose checkpoint write did not succeed, for example because it timed out while the node was under load, stopped processing events entirely. The projection neither retried the write nor faulted: it stayed in the `Writing` state indefinitely, with only an `Error while processing message ... in queued handler 'Projection Core #N'` entry in the log to indicate a problem, and had to be restarted to recover. The fix restores the original behaviour: the checkpoint write is retried, and the projection is faulted once the retries are exhausted. This regression was introduced in 25.1.0.

### Persistent Subscriptions: Fixed consumers stalling after a failed parked message write (PR [#5699](https://github.com/kurrent-io/KurrentDB/pull/5699))

When a message could not be written to a subscription's parked message stream, the write was not retried and the consumer permanently lost one of its in-flight message slots. After enough failures the consumer stopped receiving messages altogether until it reconnected. Affected messages never reached the parked stream, and because the subscription checkpoint had already advanced past them, they were lost if the node restarted before the consumer reconnected. As before the regression, the write is retried, and exhausting the retries is reported with a `Possible message loss` error. This regression was introduced in 25.1.0.

## [26.1.1](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.1.1)

10 July 2026

### Important: Fixed Security vulnerability

Fixed a vulnerability ([GHSA-r8vq-5875-cq97](https://github.com/kurrent-io/KurrentDB/security/advisories/GHSA-r8vq-5875-cq97)) where privileged access to read the `$all` stream could be used to perform arbitrary file reads and GET-only server-side request forgery (SSRF).

### Projections: Fixed correctness bugs in the JavaScript state handler (PR [#5610](https://github.com/kurrent-io/KurrentDB/pull/5610))

JavaScript projections no longer fault on three unusual inputs: reading `event.body` on an event whose JSON body is a primitive (a number, string, boolean, or `null`, rather than an object); restarting a projection that returns a string as its state, or that has no explicit `when()` handlers; and state that contains `NaN` or `Infinity`, which is now persisted as `null` to match `JSON.stringify` instead of faulting.

### Projections V2: Fixed processing of link-to events that point to scavenged events (PR [#5657](https://github.com/kurrent-io/KurrentDB/pull/5657))

The Projections V2 engine no longer faults when a link read from `$all` with link resolution enabled points to an event that has been scavenged, or whose stream has been deleted. These unresolved events are now skipped.

### Secondary Indexes: Fixed statistics in UI (PR [#5651](https://github.com/kurrent-io/KurrentDB/pull/5651))

Secondary-index statistics are now shown again in the admin UI.

### Persistent Subscriptions: Fixed Parked Messages View in UI (PR [#5660](https://github.com/kurrent-io/KurrentDB/pull/5660))

When two parked messages could not be resolved (for example, because the Persistent Subscription's source stream had been truncated by `$maxCount`), the admin UI would show no parked messages, making the parked message stream appear empty.

### Persistent Subscriptions: Backported TruncateParked API (PR [#5665](https://github.com/kurrent-io/KurrentDB/pull/5665))

Added a `TruncateParked` operation (over gRPC and HTTP) that truncates a subscription's parked message stream without replaying (optionally up to a given point). Before this, the only way to discard parked messages without replaying them was to truncate the parked messages stream directly, which worked but did not update the statistics.

### Added a configurable gRPC response compression level (PR [#5670](https://github.com/kurrent-io/KurrentDB/pull/5670))

The gRPC response compression level, hardcoded to `Optimal` since it was introduced in 25.1, can now be set with the `CompressionLevel` option (default `Optimal`). Valid values, from least compressed to most compressed are: `NoCompression`, `Fastest`, `Optimal`, and `SmallestSize`. Smaller sizes use more CPU and less network.

## [26.1.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.1.0)

30 April 2026

### What's new

Find out [what's new](../quick-start/whatsnew.md) in this release.
