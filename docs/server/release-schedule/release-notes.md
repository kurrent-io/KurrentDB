---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v26.2.

## [26.2.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.2.0)

30 September 2026

### What's new

Find out [what's new](../quick-start/whatsnew.md) in this release.

### Connectors: Reduced disk reads after leader election (PR [#5743](https://github.com/kurrent-io/KurrentDB/pull/5743))

When a node becomes leader, it reads the log to determine which connectors should be running. Previously, it could reread a large portion of the log after each election, even when no connectors had changed or none were configured. The leader now records its progress regularly, so a new leader only reads recent events. The first election after upgrading may still read from the last recorded position.
