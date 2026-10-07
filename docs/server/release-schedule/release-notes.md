---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v26.2.

## [26.2.2](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.2.2)

7 October 2026

### Fixed memory leak in gRPC streaming calls (PR [#5764](https://github.com/kurrent-io/KurrentDB/pull/5764))

In 26.2.0 and 26.2.1, memory usage grew by a few bytes with each gRPC streaming call, such as streaming reads and subscriptions, and was not reclaimed when the call ended. Memory usage now remains stable.

### Subscriptions: Added opt-in FellBehind notifications for gRPC clients (PR [#5760](https://github.com/kurrent-io/KurrentDB/pull/5760))

When a live subscription cannot keep up and drops back to catch-up mode, the server can now send a `FellBehind` message to the client, with a timestamp and checkpoint, just as it already sends `CaughtUp`. Clients only receive it if they opt in by requesting compatibility level 2 on the subscription, so existing clients see no change.

## [26.2.1](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.2.1)

5 October 2026

### Schema Registry: Fixed queries missing recently registered schemas (PR [#5758](https://github.com/kurrent-io/KurrentDB/pull/5758))

In 26.2.0, schema registry queries, such as listing schemas or checking compatibility, could miss schemas registered or changed while the node was running. When this happened, the logs showed an `ArgumentNullException` for `sequenceId` from `DuckDBProjector`. Queries now stay up to date, and after upgrading they also pick up any schemas they missed.

### Scavenging: Fixed scavenge on older Linux distributions (PR [#5756](https://github.com/kurrent-io/KurrentDB/pull/5756))

26.2.0 could not run scavenges on RHEL 8 or Ubuntu 20.04. The scavenge would immediately fail due to the version of glibc. This has been fixed.

## [26.2.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.2.0)

30 September 2026

### What's new

Find out [what's new](../quick-start/whatsnew.md) in this release.

### Connectors: Reduced disk reads after leader election (PR [#5743](https://github.com/kurrent-io/KurrentDB/pull/5743))

When a node becomes leader, it reads the log to determine which connectors should be running. Previously, it could reread a large portion of the log after each election, even when no connectors had changed or none were configured. The leader now records its progress regularly, so a new leader only reads recent events. The first election after upgrading may still read from the last recorded position.
