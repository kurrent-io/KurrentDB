---
order: 1
---

# Release notes

This page contains the release notes for EventStoreDB 24.10

## [24.10.10](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.10)

16 January 2025

### Projections: Fixed wake-up race condition (PR [#5454](https://github.com/kurrent-io/KurrentDB/pull/5454)) 

When writing empty transactions (write requests with 0 events in) a race condition existed where a projection that had reached the end of its input stream and stopped might not detect the addition of a new event. The new event could remain unprocessed until another event is written to any stream. Subsequent new events written to any stream would allow the projection to continue and process any outstanding events correctly. Writing empty transactions is uncommon but supported by the database.

### Connectors: Fix position tracker not accounting for duplicate track calls
The position tracker in connectors has been updated to correctly handle duplicate track calls. Previously, duplicate calls to track the same position would cause an IndexOutOfRangeException. This fix ensures that duplicate track calls are safely ignored, improving connector reliability.


## [24.10.9](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.9)

11 December 2025

### Connectors: Performance improvements

Improved connector performance through a new position tracker and optimized asynchronous operations. Memory allocations and overhead are reduced, particularly when operations complete synchronously.

### Connectors: Kafka sink waitForBrokerAck setting now defaults to false

The Kafka sink connector now uses callback-based message delivery by default instead of synchronous sends. The `waitForBrokerAck` setting defaults to false. The connector maintains checkpoints to ensure at least once delivery.

### Connectors: Clear error details on successful recovery and stop of connectors 

Connector error details are now properly cleared when a connector successfully recovers or is stopped.

### Connectors: Enable query without request body and fix pagination bug
Connector listing now uses query parameters instead of requiring a request body. Also fixes a pagination bug.

### Connectors: Record state change on activation and deactivation failures

Connector state now correctly records as `deactivated` when activation fails before the processor starts.

### Connectors: Fixed command service logging issue 

Resolved an invalid operation exception triggered when sending commands to the connectors service over gRPC.

### Connectors: Use position-based filtering when reading events

Connectors now use position-based filtering instead of stream revision-based reads. This fixes system events being incorrectly included in connector results and improves error handling when reading from non-existent streams.

### Connectors: Headers improvements

Header keys now retain their original casing when delivered to connector destinations. The default headers `esdb-record-partition-key` and `esdb-record-is-transformed` are no longer added to outgoing messages. You can also choose whether to include system headers in sink metadata.

## [24.10.8](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.8)

27 November 2025

### Fixed persistent subscription stall if client sends no Acks or Naks (PR [#5384](https://github.com/kurrent-io/KurrentDB/pull/5384))

Previously, if a client is subscribed with a persistent subscription but neither ACKS nor NAKS any events, the server would continue to retry them per the persistent subscription configuration and then eventually park them. However, eventually the buffer would become empty and the subscription would stall until the leader changes or the subsystem is restarted. With the default settings it would take 4 hours of no ACKS or NAKS for the buffer to empty.

The behavior is now fixed so the server will continue sending and parking messages.

Note that the stall can only occur if the client does not send any ACKS or NAKS, which is an indicator of a problem in the client code.

## [24.10.7](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.7)

05 September 2025

### Removed logging to Windows Event Log when running as a windows service (PR [#5253](https://github.com/kurrent-io/KurrentDB/pull/5253))

Running as a Windows Service was introduced in 24.10.6. When running as a Windows Service logs were also outputted to the Windows Event Log. We have removed this extra log output to avoid the duplicated logging and also to avoid some logging failures that could occur when logging to the Windows Event Log during server shutdown, which could result in worrying (but harmless) errors on shutdown.

### Add extra logging when UnwrapEnvelopeMessage is slow (PR [#5198](https://github.com/kurrent-io/KurrentDB/pull/5198))

When UnwrapEnvelopeMessage triggers a 'SLOW QUEUE MESSAGE` log, it now includes the name of the action it was unwrapping.

### Lower Scavenge API GET calls to Verbose (#5197) (PR [#5204](https://github.com/kurrent-io/KurrentDB/pull/5204))

The auto-scavenge checks on the status of in-progress scavenges frequently, which was producing unnecessary logs.

## [24.10.6](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.6)

07 July 2025

### Added logging for significant garbage collections (PR [#5161](https://github.com/kurrent-io/KurrentDB/pull/5161))

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

### Added support for running as a Windows service. (PR [#5163](https://github.com/kurrent-io/KurrentDB/pull/5163))

The server can now be installed as a Windows Service.

### Added server configuration option for TCP read expiry (PR [#5166](https://github.com/kurrent-io/KurrentDB/pull/5166))

The option is `TcpReadTimeoutMs` and it defaults to 10000 (10s, which matches the previous behavior).

It applies to reads received via the TCP client API. When a read has been in the server queue for longer than this, it will be discarded without being executed. If your TCP clients are configured to timeout after X milliseconds, it is advisable to set this server option to be the same, so that the server will not execute reads that the client is no longer waiting for.

For gRPC clients, the server-side discarding is already driven by the deadline on the read itself without requiring server configuration

### Fixed: Do not throw if io stat is disabled in the Linux kernel (PR [#5107](https://github.com/kurrent-io/KurrentDB/pull/5107))

This allows stats to be collected in environments where `/proc/self/io` is disabled. e.g. Azure Container Instances

### Connectors: Disable Leases

Leases are now disabled by default since we only run on the leader. The stream `$connectors/{id}/leases` is no longer created when a connector is created.

### Connectors: Track writes for Serilog and Elasticsearch connector

The Serilog and Elasticsearch connectors previously did not track the number of writes they made to the sink. This has now been added, and the metric `kurrent_sink_written_total_records` will now include the number of writes made by the serilog and elasticsearch connectors.

## [24.10.5](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.5)

19 May 2025

### Fix filtered $all subscriptions checkpoint behavior (PR [#4893](https://github.com/kurrent-io/KurrentDB/pull/4893) and [#4990](https://github.com/kurrent-io/KurrentDB/pull/4990))

Fixed an issue with filtered subscriptions where it was possible to incorrectly send a checkpoint of `(ulong.max, ulong.max)` to a filtered $all subscription if:

* the filtered subscription started from the beginning of the database and
* none of the events in the database matched the filter and
* the checkpoint interval was greater than the number of events in the database.

The checkpoint will now be updated to a valid value before sending it when transitioning to live.

### Projections: Fix metrics not reporting faulted projections (PR [#5022](https://github.com/kurrent-io/KurrentDB/pull/5021))

Fixed the projection metrics not reporting the projection status when it had a compound state (e.g., `faulted (enabled)`).

### Persistent Subscriptions: Add additional metrics for parked messages (PR [#5062](https://github.com/kurrent-io/KurrentDB/pull/5062))

Added two persistent subscription metrics to count the number of parked message requests and replays.

```
eventstore_persistent_sub_park_message_requests
eventstore_persistent_sub_parked_message_replays 
```

The park message requests are subdivided into two reason categories: `client-nak` and `max-retries`.

### Connectors: Add metrics (PR [#5007](https://github.com/kurrent-io/KurrentDB/pull/5007))

Added `Kurrent`, `Kurrent.Connectors` and `Kurrent.Connectors.Sinks` meters. The following metrics are now available for connectors:

```
kurrent_connector_active_total
kurrent_sink_written_total_records
kurrent_sink_errors_total
kurrent_sink_transform_duration_milliseconds
messaging_kurrent_consumer_message_count_total
messaging_kurrent_consumer_commit_latency
messaging_kurrent_consumer_lag
messaging_kurrent_producer_queue_length
messaging_kurrent_producer_message_count_total
messaging_kurrent_producer_produce_duration_milliseconds
messaging_kurrent_processor_error_count_total
```

These metrics are described in the [connectors documentation](../features/connectors/metrics.md).

### Connectors: Add data protection

We've implemented a data protection system to secure sensitive connector
configurations. All connectors now use envelope encryption to protect sensitive
data like passwords and tokens, ensuring secure transmission. Setup is simple,
with token-based protection that can be configured directly or through separate
files for added security in production.

### Connectors: Include deleted connectors in results

Previously, deleted connectors were unintentionally omitted from the results, leading to incomplete data visibility. This fix ensures that both deleted and non-deleted connectors are properly included where applicable.

### Connectors: Added validation for Serilog connector configuration

Added validation for Serilog configuration during setup or changes. This helps catch errors early and prevents issues with logging functionality.

### Connectors: Ensure reset connector command starts at offset 0

Previously, executing the reset connector command did not correctly reset the offset to zero, leading to inconsistent data replay. This fix ensures the connector starts from the beginning as expected when reset.

### Connectors: Rename connectors registry snapshot stream

A system stream for connectors was incorrectly named `$$connectors-ctrl/registry-snapshots` instead of `$connectors-ctrl/registry-snapshots` and therefore treated as a metadata stream.

This has been renamed, and the empty `connectors-ctrl/registry-snapshots` stream will be deleted when upgrading from older versions of 24.10 to remove the incorrect metadata stream.

## [24.10.4](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.4)

6 March 2025

### Reduce allocations when projections validate JSON (PR [#4881](https://github.com/kurrent-io/KurrentDB/pull/4881))

This reduces memory pressure when running projections, especially on databases with larger events.

There is a change in behaviour between 24.10.4 and previous versions.

Previously unquoted keys, e.g., `{ foo : "bar" }`, were erroneously considered valid JSON and would be processed by the projections. This is not the case starting from 24.10.4.

Projections won't fault after an upgrade if they previously emitted an event based on a now-invalid event.

### Add an option to limit the size of events appended over gRPC and HTTP (PR [#4882](https://github.com/kurrent-io/KurrentDB/pull/4882))

The `MaxAppendEventSize` option has been added to limit the size of individual events in an append request over gRPC and HTTP. The default for this new option is `int.MaxValue` (no limit). Events appended using the AtomPub HTTP API still may not exceed 4 MB. Events written by internal systems or services, such as projections, are unaffected.

This option differs from `MaxAppendSize`, which limits the size of entire append requests over gRPC.

A new RPC exception `maximum-append-event-size-exceeded` was added as part of this, and is returned when any event in an append batch exceeds the configured `MaxAppendEventSize`.

**Note:** The calculation of event size for events appended over gRPC was fixed as part of this, which could result in a change in behaviour for appending batches of events over gRPC. Previously, the event size was calculated off of the event's data, excluding the size of the event's metadata or event type. This means that append requests which previously did not trip the `MaxAppendSize` check could fail on 24.10.4.

### Add an option to limit the size of the projection state (PR [#4884](https://github.com/kurrent-io/KurrentDB/pull/4884))

The `MaxProjectionStateSize` option has been added to configure the maximum size of a projection's state before the projection should fault. This defaults to `int.MaxValue` (no limit).

A projection will now fault if the total size of the projection's state and result exceeds the `MaxProjectionStateSize`. This replaces the warning log for state size in `CoreProjectionCheckpointManager`.

**Note:** Sometimes, a projection's state and result will be set to the same value and written to the projection state event twice. This can result in the `MaxProjectionStateSize` being exceeded even when the projection state is half the configured maximum size.

## [24.10.3](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.3)

27 February 2025

### Fix getting projection statistics over gRPC for faulted projections (PR #4863)

Before this fix, projection statistics could not be retrieved via gRPC if one of the projections immediately transitions into a faulted state.

### Fix possibility of a gRPC write hanging clientside if a leader election occurs (PR #4858)

Prior to this fix, if a gRPC write was pending at the time of a leader election, the gRPC call could be terminated successfully instead of in a failed state. On the dotnet client, this results in the Append Task call hanging indefinitely; other clients may also be affected.

## [24.10.2](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.2)

13 February 2025

### Handle replayed messages when retrying events in a persistent subscription (PR #4777)

This fixes an issue with persistent subscriptions where retried messages may be missed if they are retried after a parked message is already in the buffer. This can happen if a user triggers a replay of parked messages while there are non-parked messages timing out and being retried.

When this occurs, an error is logged for each message that is missed:

```
Error while processing message EventStore.Core.Messages.SubscriptionMessage+PersistentSubscriptionTimerTick in queued handler 'PersistentSubscriptions'.
System.InvalidOperationException: Operation is not valid due to the object's current state.
```

If a retried message is missed in this way, the consumer will never receive it. The persistent subscription will need to be reset in order to recover and receive these messages again.

### Validate against attempts to set metadata for the "" stream (PR #4799)

An empty string (“”) has never been a valid stream name. Attempting to set the metadata for it results in an attempt to write to the stream “$$”, which, until now, has been a valid stream name.

However, writing to “$$” involves checking on the “” stream, to see if it is soft deleted. This results in the storage writer exiting, which shuts down the server to avoid a 'sick but not dead' scenario

“$$” is now an invalid stream name, and so any attempt to write to it is rejected at an early stage.

### Fix EventStoreDB being unable to start on Windows 2016 (PR #4765)

EventStoreDB would crash when running on Windows 2016 with the following error:

```
[ 1488, 1,16:45:11.368,FTL] EventStore Host terminated unexpectedly.
System.TypeInitializationException: The type initializer for 'EventStore.Core.Time.Instant' threw an exception.
---> System.Exception: Expected TicksPerSecond (1853322) to be an exact multiple of TimeSpan.TicksPerSecond (10000000)
at EventStore.Core.Time.Instant..cctor() in D:\a\TrainStation\TrainStation\EventStore\src\EventStore.Core\Time\Instant.cs:line 21
--- End of inner exception stack trace ---
```

This was caused by a bug when converting ticks between Stopwatch.Frequency and TimeSpan.TicksPerSecond.

### Fixes to stats and metrics

Fixed the following issues:

- On Linux, the disk usage/capacity were showing the values for the disk mounted at `/` even if the database was on a different disk (PR #4759)
- Align histogram buckets with the dashboard (PR #4811)
- Fix some disk IO stats being 0 on Linux due to the assumption that the `/proc/<pid>/io` file was in a particular order and returned early without reading all the values (PR #4818)

### Support paging in persistent subscriptions (PR #4785)

The persistent subscription UI now pages when listing all persistent subscriptions rather than loading all of them at once. By default, the UI shows the persistent subscription groups for the first 100 streams and refreshes every second. These options can be changed in the UI.

A count and offset can now be specified when getting all persistent subscription stats through the HTTP API: `/subscriptions?count={count}&offset={offset}`.

The response of `/subscriptions` (without the query string) is unchanged.

## [24.10.1](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.1)

18 December 2024

### Up to 100 license entitlements are fetched (PR #4670)

Previously, only ten license entitlements were returned, meaning that entitlements were missing on the commercial license policies with more than ten entitlements.

If you are using a commercial trial of 24.10, you must upgrade to 24.10.1 to transition onto the full license.

### SLOW QUEUE messages are logged by default again (PR #4660)

Slow bus messages were still logged, but the logging of slow queue messages had been accidentally disabled by default.

### Update dependency patch version due to CVEs (PR #4674)

`System.Private.Uri` has been upgraded from 4.3.0 to 4.3.2 (patch upgrade) due to `GHSA-5f2m-466j-3848` and `GHSA-xhfc-gr8f-ffwc`

## [24.10.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.0)

20 November 2024

Find out [What's new](../quick-start/whatsnew.md) in this release.

## [24.6.0](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v24.6.0)

12 July 2024

### Critical fix - Events in explicit transactions can be missing from $all

Events written in explicit transactions via a TCP client can be missing from $all reads and subscriptions.

These events are present when reading from a specific stream but may be absent when reading from $all. This happens only with events which were written using transactions on the TCP client.

* This bug only affects events written in explicit transactions via the deprecated TCP API
* The bug appears to affect Filtered all reads when the window is bigger than the maxcount
* The bug can affect regular all reads/subscriptions

After the fix, the events are no longer missing from $all reads and subscriptions.

This is also in v23.10.2 and v22.10.6

### Breaking changes

#### The database now restarts after a successful truncation operation

It is expected that EventStoreDB is run with a process manager like Systemd to automatically restart the process if it shuts down. This is only a breaking change if your process manager is configured to not restart the process if it shuts down twice within a short period of time.

A node will now restart after a truncation operation. This will affect the behaviour of the node in the following cases:

#### Deposed leaders may shut down twice before they rejoin the cluster

When a leader loses connection to the rest of the cluster, the remaining nodes may elect a new leader among themselves. When the former leader rejoins the cluster, it may go offline to truncate uncommitted records from its log so that it can subscribe to the new leader.

The node will now restart a second time after truncation has completed successfully.

#### Nodes will restart after performing a file copy restore.

Part of the file copy backup and restore procedure requires copying the chaser checkpoint file over the truncate checkpoint file. This triggers the database to truncate the log on the next startup.

The node will restart after the truncation has completed.

#### Unbuffered config setting now has no effect

The `Unbuffered` config setting is deprecated and now has no effect.

[Contact us](https://www.kurrent.io/contact) if you're using this feature.

#### Persistent subscription checkpoint event type name change

The event type for persistent subscription checkpoints has been updated from `SubscriptionCheckpoint` to `$SubscriptionCheckpoint`. Existing checkpoints written before upgrading will still retain the old event type and remain fully functional.

This will only affect users who read from or subscribe to the persistent subscription checkpoint streams or $all, and consume those events directly. The persistent subscriptions themselves are unaffected and will continue to function as usual.

### Performance improvements

#### Scavenged databases now open much faster on startup

Midpoints for scavenged chunks are now built later on demand. This significantly improves the performance of opening a large scavenged database.

#### Reduced FileHandle usage by 80%

This makes it less likely for the database to hit the file limit, and lowers the memory footprint of EventStoreDB.

### In this release

#### Metrics

* Improved system metrics so that the following are available:
    * CPU Usage on Linux, FreeBSD, OSX, Windows
    * CPU Load Averages on Linux, FreeBSD, OSX
    * Disk IO Stats on Linux, Windows and OSX (read bytes and written bytes only)
* Added a metric to track the number of elections, allowing users to configure alerts if the count exceeds a defined threshold within a given time period.
* Added metrics for projection subsystem:
    * Projection status
    * Percent progress
    * Events processed since restart
* Added metrics for Persistent Subscriptions:
    * Total items processed
    * Connection count
    * Total in-flight messages
    * Total number of parked messages
    * Last seen and/or checkpointed event

#### Quality of life improvements

* EventStoreDB can now build and run on OSX for development purposes.
* Index merge will now continue without bloom filters if there is not enough memory to store them.
* Use the advertised addresses for identifying nodes in the scavenge log.
* Change status of incomplete scavenges from "Failed" to "Interrupted".
* Log an error when the node certificate does not have the necessary usages.
* Add more information to the current database options API response.
* Allow statusCode for health requests to be provided in the query string. e.g. `/health/live?liveCode=200`
* Projections will now log a warning when the projection state size becomes greater than 8 MB.

## [24.2.0](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v24.2.0)

29 February 2024

### New plugins

#### Connectors plugin preview

EventStoreDB 24.2.0 introduces a preview of the Connectors plugin, designed to streamline the integration of EventStoreDB with external services. Connectors allow for the filtering and forwarding of events directly to downstream services and remove the need for manual subscription or checkpoint management.

Each connector runs server-side, maintains its own checkpoints, and can be configured to run on nodes with a specific role (i.e., Leader, Follower, or ReadOnlyReplica).

The preview includes two sinks:
* Console Sink: For testing and development purposes.
* HTTP Sink: For POSTing events to an HTTP endpoint of an external system.

Refer to [the documentation](../features/connectors/README.md) for instructions on setting up and configuring connectors and sinks.

#### x.509 user certificates commercial plugin

The new User Certificates plugin enables the use of x.509 user certificates to authenticate users on a cluster.

In this initial version of the plugin, x.509 certificate authentication works in addition to the basic authentication method. This allows you to continue using basic authentication while gradually transitioning to x.509 certificate authentication if desired.

This plugin is available in the commercial version of 24.2.0 and is disabled by default. Refer to [the documentation](../security/user-authentication.md#user-x509-certificates) for more information on how to use this feature.

#### Logs download commercial plugin

The Logs Endpoint plugin provides you with the ability to list and download log files for an EventStoreDB instance over HTTP.

This allows developers and users without direct access to the machines where the log files are stored to easily list and download log files for diagnostic purposes.

This plugin is part of the commercial version in 24.2.0 and is enabled by default. Refer to [the documentation](../diagnostics/logs.md#logs-download) for more information on how to use this feature.

#### OpenTelemetry exporter commercial plugin

The Open Telemetry Exporter plugin allows  you to export EventStoreDB metrics via the Open Telemetry Protocol to a specified endpoint.
This direct export capability simplifies integration with Application Performance Monitoring (APM) providers, enhancing visibility into EventStoreDB operations without additional configuration overhead.

Refer to [the documentation](../diagnostics/integrations.md#opentelemetry-exporter) for more information about using and configuring this plugin.

### Feature Enhancements

#### Catch-up Subscription Improvements

Version 24.2.0 introduces significant improvements to catch-up subscriptions over gRPC, including a seamless transition mechanism between live and catch-up modes.

This update addresses the previous challenge, where catch-up subscriptions that fell behind would be dropped by the server with the message "Consumer too slow". The client would then need to resubscribe from the last checkpoint to continue receiving events.

As of 24.2.0, catch-up subscriptions automatically revert to catch-up mode on the server side without user intervention, improving reliability and consistency.

Additionally, subscriptions can now reauthorize the user running the subscription in response to user access changes. This means if you remove a user's access to a stream (for example, through ACLs on the stream), any subscriptions that the user has to that stream will be dropped.

#### Better support for containerized environments

EventStoreDB 24.2.0 is now able to detect when it's running in a containerized environment and will disable certain auto-configuration options. This helps prevent the node from running out of resources and allows for more fine-tuned control of the EventStoreDB instances.

### Breaking changes

#### External TCP API Removed
The deprecated external TCP API has been removed in 24.2.0. This means that any external clients using the TCP API will no longer work with EventStoreDB versions 24.2.0 and onwards.

A number of configuration options have been removed as part of this. EventStoreDB will not start by default if any of the following options are present in the database configuration:
* AdvertiseTcpPortToClientAs
* DisableExternalTcpTls
* EnableExternalTcp
* ExtHostAdvertiseAs
* ExtTcpHeartbeatInterval
* ExtTcpHeartbeatTimeout
* ExtTcpPort
* ExtTcpPortAdvertiseAs
* NodeHeartbeatInterval
* NodeHeartbeatTimeout
* NodeTcpPort
* NodeTcpPortAdvertiseAs

The options for the internal TCP API (`ReplicationTcp*`/`IntTcp*`) are unchanged from version 23.10.0.
