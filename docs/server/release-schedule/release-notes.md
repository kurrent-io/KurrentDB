---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v26.0.

## [26.0.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.0.0)

16 January 2026

### What's new

Find out [what's new](../quick-start/whatsnew.md) in this release.

### Projections: Fixed wake-up race condition (PR [#5428](https://github.com/kurrent-io/KurrentDB/pull/5428)) 

When writing empty transactions (write requests with 0 events in) a race condition existed where a projection that had reached the end of its input stream and stopped might not detect the addition of a new event. The new event could remain unprocessed until another event is written to any stream. Subsequent new events written to any stream would allow the projection to continue and process any outstanding events correctly. Writing empty transactions is uncommon but supported by the database.
