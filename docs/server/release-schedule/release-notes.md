---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v25.1.

## [25.1.4](https://github.com/EventStore/EventStore/releases/tag/v25.1.4)

23 February 2026

### Small improvements to OAuth plugin (PR [#5517](https://github.com/kurrent-io/KurrentDB/pull/5517))

The new setting `OAuth::DisableCodeChallengeMethodsSupportedValidation` disables validation of the `code_challenge_methods_supported` field in the identity provider's discovery document.

Enable this when using an identity provider such as Microsoft Entra that supports PKCE but does not advertise it in the discovery document.

The OAuth and Ldaps plugins are now also able to read configuration settings from environment variables and/or command line options. Previously, and in contrast to all the other configuration options, the OAuth and Ldaps plugins could only take their configuration from a file, and not from the environment or command line options.

## [25.1.3](https://github.com/EventStore/EventStore/releases/tag/v25.1.3)

6 February 2026

### Fixed "Data sizes violation" error (PR [#5492](https://github.com/kurrent-io/KurrentDB/pull/5492))

Under certain conditions a follower or Read Only Replica could fail to join a cluster with a "Data sizes violation" error.

This fix resolves the error condition and allows the node to join the cluster. There is no risk to the integrity of the data.

### Fixed potential memory leak (PR [#5426](https://github.com/kurrent-io/KurrentDB/pull/5426))

A way to trigger the leak in practice has not been identified, but nonetheless it has been fixed in this patch.

### Fixed persistent subscription stall if client sends no Acks or Naks (PR [#5383](https://github.com/kurrent-io/KurrentDB/pull/5383))

Previously, if a client is subscribed with a persistent subscription but neither ACKS nor NAKS any events, the server would continue to retry them per the persistent subscription configuration and then eventually park them. However, eventually the buffer would become empty and the subscription would stall until the leader changes or the subsystem is restarted. With the default settings it would take 4 hours of no ACKS or NAKS for the buffer to empty.

The behavior is now fixed so the server will continue sending and parking messages.

Note that the stall can only occur if the client does not send any ACKS or NAKS, which is an indicator of a problem in the client code.

### Lower the memory restriction on DuckDB (PR [#5404](https://github.com/kurrent-io/KurrentDB/pull/5404))

To 25% of available memory.

### Improved connectors performance (PR [#5389](https://github.com/kurrent-io/KurrentDB/pull/5389))

## 25.1.2

Not published, changes included in 25.1.3.

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
