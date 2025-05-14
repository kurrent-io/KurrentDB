---
order: 1
---

# Release Notes

This page contains the release notes for EventStoreDB 24.10

## [24.10.5](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.5)

6 May 2025

### Fix filtered $all subscriptions checkpoint behaviour (PR [#4893](https://github.com/kurrent-io/KurrentDB/pull/4893) and [#4990](https://github.com/kurrent-io/KurrentDB/pull/4990))

Fixed an issue with filtered subscriptions where it was possible to incorrectly send a checkpoint of `(ulong.max, ulong.max)` to a filtered $all subscription if:

* the filtered subscription started from the beginning of the database and
* none of the events in the database matched the filter and
* the checkpoint interval was greater than the number of events in the database.

The checkpoint will now be updated to a valid value before sending it when transitioning to live.

Fixed a regression where filtered $all subscriptions could send an incorrect checkpoint equal to the end position of the log when an empty page was read. This could cause the subscription to miss an event if it was resumed using only the commit position.

### Fix metrics not reporting faulted projections (PR [#5022](https://github.com/kurrent-io/KurrentDB/pull/5021))

Fixed the projection metrics not reporting the projection status when it had a compound state (e.g., `faulted (enabled)`).

### Add metrics for Connectors (PR [#5007](https://github.com/kurrent-io/KurrentDB/pull/5007))

Added `Kurrent`, `Kurrent.Connectors` and `Kurrent.Connectors.Sinks` meters.

### Add checkpoint to 'CaughtUp' and 'FellBehind' messages (PR [#5034](https://github.com/kurrent-io/KurrentDB/pull/5034))

Added the checkpoint and timestamp to `CaughtUp` and `FellBehind` messages in the streams proto.
This change is backwards compatible.

## [24.10.4](https://github.com/kurrent-io/KurrentDB/releases/tag/v24.10.4)

6 March 2025

### Reduce allocations when projections validate JSON (PR [#4881](https://github.com/kurrent-io/KurrentDB/pull/4881))

This reduces memory pressure when running projections, especially on databases with larger events.

There is a change in behaviour between 24.10.4 and previous versions.

Previously unquoted keys, e.g., `{ foo : "bar" }`, were erroneously considered valid JSON and would be processed by the projections. This is not the case starting from 24.10.4.

Projections won’t fault after an upgrade if they previously emitted an event based on a now-invalid event.

### Add an option to limit the size of events appended over gRPC and HTTP (PR [#4882](https://github.com/kurrent-io/KurrentDB/pull/4882))

The `MaxAppendEventSize` option has been added to limit the size of individual events in an append request over gRPC and HTTP. The default for this new option is `int.MaxValue` (no limit). Events appended using the AtomPub HTTP API still may not exceed 4 MB. Events written by internal systems or services, such as projections, are unaffected.

This option differs from `MaxAppendSize`, which limits the size of entire append requests over gRPC.

A new RPC exception `maximum-append-event-size-exceeded` was added as part of this, and is returned when any event in an append batch exceeds the configured `MaxAppendEventSize`.

**Note:** The calculation of event size for events appended over gRPC was fixed as part of this, which could result in a change in behaviour for appending batches of events over gRPC. Previously, the event size was calculated off of the event's data, excluding the size of the event's metadata or event type. This means that append requests which previously did not trip the `MaxAppendSize` check could fail on 24.10.4.

### Add an option to limit the size of the projection state (PR [#4884](https://github.com/kurrent-io/KurrentDB/pull/4884))

The `MaxProjectionStateSize` option has been added to configure the maximum size of a projection's state before the projection should fault. This defaults to `int.MaxValue` (no limit).

A projection will now fault if the total size of the projection’s state and result exceeds the `MaxProjectionStateSize`. This replaces the warning log for state size in `CoreProjectionCheckpointManager`.

**Note:** Sometimes, a projection’s state and result will be set to the same value and written to the projection state event twice. This can result in the `MaxProjectionStateSize` being exceeded even when the projection state is half the configured maximum size.

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

## 24.10.0

20 November 2024

Find out [What's new](../quick-start/whatsnew.md) in this release.
