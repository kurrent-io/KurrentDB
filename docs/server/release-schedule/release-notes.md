---
order: 1
---

# Release notes

This page contains the release notes for KurrentDB 25.0

## [25.0.1](https://github.com/kurrent-io/KurrentDB/releases/tag/v25.0.1)

20 May 2025

### Fix filtered $all subscriptions checkpoint behavior (PR [#4991](https://github.com/kurrent-io/KurrentDB/pull/4991))

Fixed an issue where an empty page read would cause filtered `$all` subscriptions to send an incorrect checkpoint with commit position equal to the end position of the log and prepare position 0. This can cause an event to be missed if the client stores only the commit position instead of both.

### Projections: Fix metrics not reporting faulted projections (PR [#5021](https://github.com/kurrent-io/KurrentDB/pull/5021))

Fixed the projection metrics not reporting the projection status when it had a compound state (e.g., `faulted (enabled)`).

### Persistent Subscriptions: Add additional metrics for parked messages (PR [#5062](https://github.com/kurrent-io/KurrentDB/pull/5062))

Added two persistent subscription metrics to count the number of parked message requests and replays.

```
eventstore_persistent_sub_park_message_requests
eventstore_persistent_sub_parked_message_replays 
```

The park message requests are subdivided into two reason categories: `client-nak` and `max-retries`.

### Connectors: Add metrics

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

## [25.0.0](https://github.com/kurrent-io/KurrentDB/releases/tag/v25.0.0)

25 March 2025

Find out [what's new](../quick-start/whatsnew.md) in this release.
