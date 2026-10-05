---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v26.2.

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
