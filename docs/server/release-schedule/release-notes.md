---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v25.1.

## [25.1.1](https://github.com/EventStore/EventStore/releases/tag/v25.1.1)

20 November 2025

### Fixed multi-stream-append that completes a chunk (PR [#5362](https://github.com/kurrent-io/KurrentDB/pull/5362))

A multi-stream-append that causes a new chunk to be created could result in incorrect last event numbers being calculated for the associated streams.

### Fixed multi-stream-append stream existence (PR [#5376](https://github.com/kurrent-io/KurrentDB/pull/5376))

A stream created in a multi-stream-append was not captured correctly in the stream existence filter. The stream would appear not to exist after the server was restarted.

### Clear error details on successful recovery and stop of connectors (PR [#5361](https://github.com/kurrent-io/KurrentDB/pull/5361))

Connector error details are now properly cleared when a connector successfully recovers or is stopped.

### Enable connectors query without request body and fix pagination bug (PR [#5360](https://github.com/kurrent-io/KurrentDB/pull/5360))

Connector listing now uses query parameters instead of requiring a request body. Also fixes a pagination bug.

### Record connector state change on activation and deactivation failures (PR [#5359](https://github.com/kurrent-io/KurrentDB/pull/5359))

Connector state now correctly records as `deactivated` when activation fails before the processor starts.

### Fix connectors command service logging issue (PR [#5371](https://github.com/kurrent-io/KurrentDB/pull/5371))

Resolved an invalid operation exception triggered when sending commands to the connectors service over gRPC.

## [25.1.0](https://github.com/EventStore/EventStore/releases/tag/v25.1.0)

15 October 2025

Find out [what's new](../quick-start/whatsnew.md) in this release.
