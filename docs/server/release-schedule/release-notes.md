---
order: 1
---

# Release notes

This page contains the release notes for EventStoreDB 23.10 and 23.6

## [23.10.7](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v23.10.7)

19 June 2025

### Add server configuration option for TCP read expiry (PR [#5144](https://github.com/kurrent-io/KurrentDB/pull/5144))

The option is `TcpReadTimeoutMs` and it defaults to `10000` (10s, which matches the previous behavior)

It applies to reads received via the TCP client API. When a read has been in the server queue for longer than this, it will be discarded without being executed. If your TCP clients are configured to timeout after X milliseconds, it is advisable to set this server option to be the same, so that the server will not execute reads that the client is no longer waiting for.

For gRPC clients, the server-side discarding is already driven by the deadline on the read itself without requiring server configuration.

### Add logging for significant garbage collections (PR [#5134](https://github.com/kurrent-io/KurrentDB/pull/5134))

This makes it clear from the logs if slow messages or leader elections are attributable to Garbage Collection (GC).

Execution engine (EE) suspensions longer than 48ms are logged as Information. Execution engine suspensions longer than 600ms are logged as Warnings. Full compacting GC start/end are logged as Information.

Note that the Start/End log messages may both be logged AFTER the execution engine pause has completed.

These will be logged even if the node shortly goes offline for truncation, which would likely prevent the EE suspension from appearing in the metrics.

If GC is determined as the cause of a leader election, a sensible course of action could be to reduce the Stream Info Cache Capacity (say, to the 100k traditional value) and/or consider enabling ServerGC.

example logs:
```
[34144,13,11:03:05.307,INF] Start of full blocking garbage collection at 06/06/2025 10:02:49. GC: #210548. Generation: 2. Reason: LargeObjectHeapAllocation. Type: BlockingOutsideBackgroundGC.
[34144,13,11:03:05.307,INF] End of full blocking garbage collection at 06/06/2025 10:03:05. GC: #210548. Took: 15,727ms
[34144,13,11:03:05.307,WRN] Garbage collection: Very long Execution Engine Suspension. Reason: GarbageCollection. Took: 15,727ms
```

### Add metrics for parked persistent subscription messages (PR [#4745](https://github.com/kurrent-io/KurrentDB/pull/4745))

Added two persistent subscription metrics to count the number of parked message requests and replays.

```
eventstore_persistent_sub_park_message_requests
eventstore_persistent_sub_parked_message_replays 
```

The park message requests are subdivided into two reason categories: `client-nak` and `max-retries`.

### Allow paging in persistent subscriptions UI (PR [#4762](https://github.com/kurrent-io/KurrentDB/pull/4762))

The persistent subscription UI now pages when listing all persistent subscriptions rather than loading all of them at once. By default, the UI shows the persistent subscription groups for the first 100 streams and refreshes every second. These options can be changed in the UI.

A count and offset can now be specified when getting all persistent subscription stats through the HTTP API: `/subscriptions?count={count}&offset={offset}`.

The response of `/subscriptions` (without the query string) is unchanged.

### Fix: Handle replayed messages when retrying events in a persistent subscription (PR [#4780](https://github.com/kurrent-io/KurrentDB/pull/4780))

This fixes an issue with persistent subscriptions where retried messages may be missed if they are retried after a parked message is already in the buffer. This can happen if a user triggers a replay of parked messages while there are non-parked messages timing out and being retried.

When this occurs, an error is logged for each message that is missed:

```
Error while processing message EventStore.Core.Messages.SubscriptionMessage+PersistentSubscriptionTimerTick in queued handler 'PersistentSubscriptions'.
System.InvalidOperationException: Operation is not valid due to the object's current state.
```

If a retried message is missed in this way, the consumer will never receive it. The persistent subscription will need to be reset in order to recover and receive these messages again.

### Fix: Validate against attempts to set metadata for the "" stream (PR [#4806](https://github.com/kurrent-io/KurrentDB/pull/4806))

An empty string (“”) has never been a valid stream name. Attempting to set the metadata for it results in an attempt to write to the stream “$$”, which, until now, has been a valid stream name.

However, writing to “$$” involves checking on the “” stream, to see if it is soft deleted. Being an invalid stream name, this results in the storage writer exiting, which shuts down the server to avoid a 'sick but not dead' scenario.

“$$” is now an invalid stream name, and so any attempt to write to it is rejected at an early stage.

### Fix: Getting projection statistics over gRPC for faulted projections (PR [#4865](https://github.com/kurrent-io/KurrentDB/pull/4865))

Before this fix, projection statistics could not be retrieved via gRPC if one of the projections had immediately transitioned into a faulted state.

## 23.10.6

Not published, changes included in 23.10.7.

## [23.10.5](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v23.10.5)

7 February 2025

### Don't time out internal reads required for subsystems to start

Set infinite timeouts on projection manager reads and persistent subscription startup config reads. If these reads timeout, the subsystem will not be able to start without manual intervention.

## [23.10.3](https://github.com/EventStore/EventStore/releases/tag/oss-v23.10.3)

18 September 2024

### Correct the default projection execution timeout

Fixed a bug where the ProjectionExecutionTimeout database setting was being copied into the configuration for individual projections, preventing them from being affected by further updates to the database setting.
The value was copied by EventStoreDB versions 23.10.0 - 23.10.1 and needs to be unset manually in order for the database setting to affect that projection again.

### Multi-stream projections now handle truncated input streams

Projections using `fromStreams` and `fromCategories` no longer fault when the input streams have been truncated due to `$maxAge`, `$maxCount`, or `$tb`.

### FIPS plugin renamed to MD5

The FIPS plugin was renamed to MD5 in this release.
This plugin is not included in the released package, and must be downloaded from the commercial website.

## [23.10.2](https://github.com/EventStore/EventStore/releases/tag/oss-v23.10.2)

10 July 2024

### Critical Fix - Events in explicit transactions can be missing from $all

Events written in explicit transactions via a TCP client can be missing from $all reads and subscriptions.

These events are present when reading from a specific stream but may be absent when reading from $all. This happens only with events which were written using transactions on the TCP client.

* This bug only affects events written in explicit transactions written via the deprecated TCP API
* The bug appears to affect Filtered all reads when the window is bigger than the maxcount
* The bug can affect regular all reads/subscriptions

After the fix, the events are no longer missing from $all reads and subscriptions.

### Other changes in this release

* Upgraded to .NET 8.
* Prevent a 64-bit integer overflow that could occur in very large index files (Roughly 1TB in size).
* Index merge will now continue without bloom filters if there is not enough memory to store them.
* (Commercial plugin) Redacted events no longer break replication on a follower node.

## [23.10.1](https://github.com/EventStore/EventStore/releases/tag/oss-v23.10.1)

20 February 2024

### Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684)

Fixed a potential password leak in the EventStoreDB Projections Subsystem.

Only database instances that use custom projections are affected by this vulnerability.

User passwords may become accessible to those who have access to the chunk files on disk, and users who have read access to system streams. Only users in the `$admins` group can access system streams by default.

Recommended action
* Upgrade EventStoreDB: Kurrent Cloud customers follow the instructions in the [cloud upgrade guide](https://docs.kurrent.io/cloud/dedicated/ops/#upgrading-kurrentdb-version). Otherwise, follow the instructions in the [standard upgrade guide](../quick-start/upgrade-guide.md).
* Reset the passwords for current and previous members of `$admins` and `$ops` groups.
* If a password was reused in any other system, reset it in those systems to a unique password to follow best practices.

## [23.10.0](https://github.com/EventStore/EventStore/releases/tag/oss-v23.10.0)

13 October 2023

### In this release

#### Database telemetry

The database now collects anonymous usage data from running clusters. We’ll be using this data to improve user experience and inform our future development.

You can opt out of sending telemetry by setting the `EVENTSTORE_TELEMETRY_OPTOUT` environment variable to `true`.

For more information see the [telemetry documentation](../usage-telemetry.md).

#### Allow using a wildcard for CertificateReservedCommonName

We’ve added support for using a wildcard in the `CertificateReservedCommonName` option for the cluster.

Previously, you had to use the same common name for all nodes in the cluster, or generate a wildcard certificate.

Now you can have non-wildcard certificates for each node in the cluster (e.g, `node1.mydomain.com`, `node2.mydomain.com`, `node3.mydomain.com`) and use a wildcard for the `CertificateReservedCommonName` to match all of them (e.g. `*.mydomain.com`)

The `CertificateReservedCommonName` now defaults to the common name of the node certificate. So, you now don’t need to specify this option unless you are using the wildcard mentioned above.

#### Configuration improvements

Refinements from version 23.6.0 continue, particularly renaming "External/Internal" interfaces to "Node/Replication" for clarity.

The old options have been deprecated and will be removed in 24.10 next year, but are still usable in 23.10. All of the deprecated and new options are listed in the [upgrade guide](../quick-start/upgrade-guide.md#deprecated-configuration-options).

### Breaking changes

There are some breaking changes when upgrading from 22.10 to 23.10. None of these changes prevent you from performing a rolling upgrade between these two versions.

The breaking changes are:
* gRPC Clients connecting to EventStoreDB must be authenticated (by default) (introduced in 23.6.0).
* Requests to the HTTP API must be authenticated (by default) (introduced in 23.6.0).
* PrepareCount and CommitCount options have been removed (introduced in 23.6.0).
* The Persistent Subscriptions config event type `PersistentConfig1` has been renamed to `$PersistentConfig`.
* Options prefixed with `Ext` and `Int` have been deprecated. Use the options prefixed with `Node` and `Replication` respectively.

You can read more about these breaking changes and what you should be aware of during an upgrade in the [upgrade guide](../quick-start/upgrade-guide.md).

## [23.6.0](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v23.6.0)

11 August 2023

### Prometheus metrics

We introduced more helpful and usable metrics in Prometheus format, making it easier to understand what is happening in the database, and to make better decisions about how to operate it.

These new metrics are collected in Prometheus format and exposed on the `/metrics` endpoint. You can configure Prometheus to scrape this endpoint directly, allowing you to build dashboards or alert on the metrics that you collect.

You can find a full list of the new metrics, how to configure them, and what the outputs of each look like in the [metrics documentation](../diagnostics/metrics.md).

Some of the notable new metrics are:

* The state of the node - whether the node is a Leader, a Follower, Catching Up or Read Only Replica
* The state of index operations such as rebuilding the index or index merges
* The state of the scavenge operation
* Reads and appends from gRPC clients
* Queue processing duration by message type
* Security Improvements

### Default admin and ops passwords

We want to eventually remove the default password "changeit" because having a known default password can leave EventStoreDB vulnerable if the admin and ops passwords aren’t updated.

As such, we have added new options to set the default admin and ops passwords on the first run of EventStoreDB. You can do this by setting the `EVENTSTORE_DEFAULT_ADMIN_PASSWORD` and `EVENTSTORE_DEFAULT_OPS_PASSWORD` environment variables.
These settings won’t affect a database that has already been created.

::: note
These new options can only be set by environment variables so that the passwords aren't saved in plaintext in config files.
:::

### Disable anonymous access by default

Historically, anonymous users with network access have been allowed to read/write streams that do not have access control lists. Anonymous access has also been available to the `/stats`, `/info`, and other HTTP endpoints.

Anonymous access is now disabled by default, except for the `/info` and `/ping` endpoints.
Gossip is also still anonymous by default while we update our supported clients to use authenticated gossip.

If you need to re-enable anonymous access, you can do this with the new `AllowAnonymousEndpointAccess` and `AllowAnonymousStreamAccess` options.

Check the [anonymous access to endpoints](../configuration/security.md#anonymous-access-to-endpoints) documentation for more options.

### EventStoreDB commercial version is now FIPS compliant

There is now a commercial plugin to allow EventStoreDB to run on a FIPS-compliant system. You can find instructions on how to download and use this plugin on the [commercial downloads](https://developers.eventstore.org/tools/fips/) site.

We have also updated our certificate generation tools to create certificates that work on FIPS systems to make testing easier.

### Configuration improvements

We want to make configuration of EventStoreDB easier, whether it’s through more informative logs or through better and more streamlined options.

This release has some quality-of-life improvements around configuring certificates as well as some helpful logs to identify misconfigurations in the cluster.

#### Certificate and secure cluster configuration

A number of the configuration improvements have been around certificates and identifying issues setting up a secure cluster. Some of the main ones are:

* Add support for encrypted and unencrypted PKCS8 private key files
* Set the default trusted root certificate path on Linux to `/etc/ssl/certs` so this does not need to be configured for most systems.
* Include more detailed errors and warnings about certificate mismatches, and other issues preventing a cluster from running correctly.
* Periodically log a warning when the certificate is nearing expiry.

#### Configuration quality of life

* Suggest the closest available option when a configuration option is unrecognised
* Log a warning when the versions between nodes are mismatched
* Log a warning when the connection between nodes is blocked - for example because of a firewall

### Redaction (commercial version)

Events are immutable and cannot be changed after the fact. Usually when you have an event with data that needs to be deleted you should take the following steps:

* Rewrite the stream to a new stream without the offending data
* Delete the old stream
* Run a scavenge to remove the data from disk on each node in turn

With the new scavenge algorithm introduced in 22.10, you no longer have to worry about data in the current chunk not being scavenged because the new algorithm will close the current chunk before scavenging. You can read more about scavenging in the documentation.

If you cannot do the above steps, then we have added a new tool to allow redacting events as a last resort. This tool needs to be run from the database directory of the node and can blank out all of the data in specific events.

If you want to make use of this tool, please contact us [here](https://www.kurrent.io/consulting) if you do not have commercial support, or reach out to our support team if you do.

### Breaking changes

The updates to anonymous access described above have introduced some breaking changes. We have also removed some unused options in EventStoreDB.

The breaking changes are as follows:

#### Clients must be authenticated by default

We have disabled anonymous access to streams by default in this version. This means that read and write requests from clients need to be authenticated.

If you see authentication errors when connecting to EventStoreDB after upgrading, please ensure that you are either using default credentials on the connection, or are passing user credentials in with the request itself.

If you want to revert back to the old behaviour, you can enable the `AllowAnonymousStreamAccess` option in EventStoreDB.

#### Requests to the HTTP API must be authenticated by default

Like with anonymous access to streams, anonymous access to the HTTP and gRPC endpoints has been disabled by default. The exceptions are the `/gossip`, `/info`, and `/ping` endpoints.

Any tools or monitoring scripts accessing the HTTP endpoints (e.g. `/stats`) will need to make authenticated requests to EventStoreDB.

If you want to revert back to the old behaviour, you can enable the `AllowAnonymousEndpointAccess` option in EventStoreDB.

#### PrepareCount and CommitCount options have been removed
We have removed the `PrepareCount` and `CommitCount` options from EventStoreDB. EventStoreDB will now fail if these options are present in the config on startup.

These options do not have an effect any more and were a holdover from a previous version. You can safely remove them from your configuration file if you have them defined.

### Other changes

* Support bloom filters for index files larger than 400gb.
* The EventStoreDB version has been added to the gossip, and nodes will now monitor the version of other nodes in the cluster and log when there is a version mismatch.
* Allow setting the projection execution timeout per projection rather than as a node configuration option so that this can be changed without restarting the leader.

### Other fixes

* Prevent the risk of implicit transactions being partially written when crossing a chunk boundary on the leader node.
* `FilteredAllSubscription` checkpoint now continues to update after the subscription becomes live.
* Ensure persistent subscriptions messages don't continue on the main queue. This prevents continuations which could take a long time to process from happening on the main queue.
* Fix certificates not updating when the `admin/reloadconfig` endpoint was called.
* Correct the number of behind messages shown in the persistent subscriptions UI.
* Fix projection progress report getting stuck at less than 100% even though they have completed.
* Correctly put a projection in the faulted state if the last event in the checkpoint stream is not a checkpoint.
* Don't throw an error in projections if projection streams (e.g. emitted streams or checkpoint streams) do not exist when deleting the projection.
