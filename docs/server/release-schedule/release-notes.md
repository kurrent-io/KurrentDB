---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB v26.0.

## [26.0.4](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.0.4)

27 October 2026

### Increased the maximum number of chunks (PR [#5721](https://github.com/kurrent-io/KurrentDB/pull/5721))

A database can now contain up to 600,000 logical chunks, raised from 400,000. With the default chunk size this increases the maximum logical size of the transaction log from approximately 100 TiB to 150 TiB. As before, the server logs warnings and then errors as the database approaches the limit, and shuts down if the limit is reached.

### Projections: Fixed projections stalling after a failed checkpoint write (PR [#5702](https://github.com/kurrent-io/KurrentDB/pull/5702))

A projection whose checkpoint write did not succeed, for example because it timed out while the node was under load, stopped processing events entirely. The projection neither retried the write nor faulted: it stayed in the `Writing` state indefinitely, with only an `Error while processing message ... in queued handler 'Projection Core #N'` entry in the log to indicate a problem, and had to be restarted to recover. The fix restores the original behavior: the checkpoint write is retried, and the projection is faulted once the retries are exhausted. This regression was introduced in 25.1.0.

### Persistent Subscriptions: Fixed consumers stalling after a failed parked message write (PR [#5702](https://github.com/kurrent-io/KurrentDB/pull/5702))

When a message could not be written to a subscription's parked message stream, the write was not retried and the consumer permanently lost one of its in-flight message slots. After enough failures the consumer stopped receiving messages altogether until it reconnected. Affected messages never reached the parked stream, and because the subscription checkpoint had already advanced past them, they were lost if the node restarted before the consumer reconnected. As before the regression, the write is retried, and exhausting the retries is reported with a `Possible message loss` error. This regression was introduced in 25.1.0.

### Connectors: Fixed sink resource cleanup when a connector stops unexpectedly

Connectors now release their sink resources on every stop path, including a processing error or a leadership change. Previously, cleanup only ran when a connector was stopped through the API.

### Connectors: Fixed connectors failing after reading a large number of events (PR [#5768](https://github.com/kurrent-io/KurrentDB/pull/5768))

After the leader had read roughly 500 million events, it stopped managing connectors with a `Position tracker capacity would exceed maximum (1000000)` error, and ignored requests to start or stop connectors until it was restarted or a new leader was elected. Sink connectors with a JsonPath filter had a related problem: they stopped saving their checkpoint at the first event the filter skipped, so they redelivered events after a restart and eventually failed with the same error. Both now run indefinitely and keep their checkpoints up to date.

### Connectors: Reduced disk reads after leader election (PR [#5769](https://github.com/kurrent-io/KurrentDB/pull/5769))

When a node becomes leader, it reads the log to determine which connectors should be running. Previously, it could reread a large portion of the log after each election, even when no connectors had changed or none were configured. The leader now records its progress regularly, so a new leader only reads recent events. The first election after upgrading may still read from the last recorded position.

## [26.0.3](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.0.3)

13 May 2026

This release contains some bug fixes and several backported improvements from the 26.1 short term support release that we wanted to include in the 26.0.x long term support release series.

### Backported: Multi-stream Appends Improvements

Multi-stream appends already allow writing to multiple streams in one atomic operation, with an expected version for each stream being written to which must be satisfied for the operation to be accepted. Now expected versions can be specified for streams even if they are not being written to in the operation. e.g. you can write to `streamA` conditionally upon the version of `streamB`.

Within a multi-stream append operation, it used to be required that events for a particular stream must be grouped together: it was not possible to write to `streamA`, then `streamB`, then `streamA` in one operation. This limitation has been removed. A client upgrade is necessary as well as a server upgrade.

A new expected version `SoftDeleted` has been added, which allows the operation to proceed only if the target stream is in a soft deleted state.

### Backported: Persistent Subscriptions Stability and Performance Improvements

Persistent subscriptions using the pinned strategies have improved performance under burst load.

Other conditions have been identified and fixed where a persistent subscription may not always resume after a leader election, or after a TCP client (excluding the dotnet TCP client) disconnects and reconnects.

These improvements and fixes are also being backported to the 24.10 series.

### Backported: OAuth Support for Microsoft Entra (PR [#5512](https://github.com/kurrent-io/KurrentDB/pull/5512))

<Badge type="info" vertical="middle" text="License Required"/>

The new setting `OAuth:DisableCodeChallengeMethodsSupportedValidation` disables validation of the `code_challenge_methods_supported` field in the identity provider's discovery document.

Enable this when using an identity provider such as Microsoft Entra that supports PKCE but does not advertise it in the discovery document.

See the [documentation](../security/user-authentication.md#oauth-authentication) for details.

### Backported: OAuth and Ldap Plugin Configuration (PR [#5515](https://github.com/kurrent-io/KurrentDB/pull/5515))

<Badge type="info" vertical="middle" text="License Required"/>

The OAuth and Ldap plugins are now also able to read configuration settings from environment variables and/or command line options. Previously, and in contrast to all the other configuration options, the OAuth and Ldap plugins could only take their configuration from a file.

### Backported: DisableClientAuthEkuValidation Option (PR [#5550](https://github.com/kurrent-io/KurrentDB/pull/5550))

Publicly trusted certificate authorities are moving away from issuing certificates with the `clientAuth` EKU, largely driven by changes to the [Chrome Root Program policy](https://www.chromium.org/Home/chromium-security/root-ca-policy/).

This option disables validation of the presence of the `clientAuth` EKU when a node connects to another, allowing KurrentDB to be used with new certificates issued by publicly trusted CAs.

See the [documentation](../security/protocol-security.md#disable-client-authentication-eku-validation) for details.

### Backported: Projections Access Created Property (PR [#5520](https://github.com/kurrent-io/KurrentDB/pull/5520))

JavaScript projections can now access the created property on events, which is the ISO 8601 timestamp of when the event was written to the database. This applies to the traditional engine and also the experimental V2 engine.

### Fixed: Daily memory/disk spike on large databases (PR [#5558](https://github.com/kurrent-io/KurrentDB/pull/5558))

Some queries were being run every 24h for telemetry which would cause a significant spike in memory and disk activity on large databases. The queries checked for the count of streams/categories/event types, the existence of explicit transactions, and the length of the longest streams.

### Fixed: Three long standing JavaScript projection state handling bugs (PR [#5623](https://github.com/kurrent-io/KurrentDB/pull/5623))

- Projections reading `event.body` no longer fault on events with primitive JSON bodies (numbers, strings, booleans, null, as opposed to objects).

- Projections that return string state, and projections defined without explicit when() handlers, now persist and reload state correctly across restarts instead of faulting.

- Projection state containing `NaN` or `Infinity` is now persisted as `null` (matching `JSON.stringify`) instead of faulting the projection.


## [26.0.2](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.0.2)

12 March 2026

### Added DisableClientAuthEkuValidation option (PR [#5550](https://github.com/kurrent-io/KurrentDB/pull/5550))

Publicly trusted certificate authorities are moving away from issuing certificates with the `clientAuth` EKU, largely driven by changes to the [Chrome Root Program policy](https://www.chromium.org/Home/chromium-security/root-ca-policy/).

This option disables validation of the presence of the `clientAuth` EKU when a node connects to another, allowing KurrentDB to be used with new certificates issued by publicly trusted CAs.

See the [documentation](../security/protocol-security.md#disable-client-authentication-eku-validation) for details.

## [26.0.1](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.0.1)

6 February 2026

### Fixed "Data sizes violation" error (PR [#5492](https://github.com/kurrent-io/KurrentDB/pull/5492))

Under certain conditions a follower or Read Only Replica could fail to join a cluster with a "Data sizes violation" error.

This fix resolves the error condition and allows the node to join the cluster. There is no risk to the integrity of the data.

### Fixed GZIP compression compatibility for empty responses (PR [#5480](https://github.com/kurrent-io/KurrentDB/pull/5480))

Empty payloads now use stored blocks instead of static Huffman blocks to fix DEFLATE errors with strict GZIP parsers.

## [26.0.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v26.0.0)

16 January 2026

### What's new

Find out [what's new](../quick-start/whatsnew.md) in this release.

### Projections: Fixed wake-up race condition (PR [#5428](https://github.com/kurrent-io/KurrentDB/pull/5428)) 

When writing empty transactions (write requests with 0 events in) a race condition existed where a projection that had reached the end of its input stream and stopped might not detect the addition of a new event. The new event could remain unprocessed until another event is written to any stream. Subsequent new events written to any stream would allow the projection to continue and process any outstanding events correctly. Writing empty transactions is uncommon but supported by the database.
