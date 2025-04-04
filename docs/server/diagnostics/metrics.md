---
order: 2
---

# Metrics

KurrentDB collects metrics in [Prometheus format](https://prometheus.io/docs/instrumenting/exposition_formats/#text-based-format), available on the `/metrics` endpoint. Prometheus can be configured to scrape this endpoint directly. The metrics are configured in `metricsconfig.json` which is located in the installation directory.

In addition, KurrentDB can actively export metrics to a specified endpoint using the [OpenTelemetry Protocol](https://opentelemetry.io/docs/specs/otel/protocol/) (OTLP). <Badge type="info" text="License Required" vertical="middle"></Badge>

A [Cluster Summary](https://grafana.com/grafana/dashboards/22824) Grafana dashboard is available, as well as [miscellaneous panels](https://grafana.com/grafana/dashboards/22823).

## Metrics reference

### Caches

#### Cache hits and misses

KurrentDB tracks cache hits/misses metrics for `stream-info` and `chunk` caches.

| Time series                                                                     | Type                     | Description                             |
|:--------------------------------------------------------------------------------|:-------------------------|:----------------------------------------|
| `kurrentdb_cache_hits_misses_total{cache=<CACHE_NAME>,kind=<"hits"\|"misses">}` | [Counter](#common-types) | Total hits/misses on _CACHE_NAME_ cache |

Example configuration:
```json
"CacheHitsMisses": {
  "StreamInfo": true,
  "Chunk": false
}
```

Example output:
```
# TYPE kurrentdb_cache_hits_misses_total counter
kurrentdb_cache_hits_misses_total{cache="stream-info",kind="hits"} 104329 1688157489545
kurrentdb_cache_hits_misses_total{cache="stream-info",kind="misses"} 117 1688157489545
```

#### Dynamic cache resources

Certain caches that KurrentDB uses are dynamic in nature i.e. their capacity scales up/down during their lifetime. KurrentDB records metrics for resources being used by each such dynamic cache.

| Time series                                                                     | Type                   | Description                                          |
|:--------------------------------------------------------------------------------|:-----------------------|:-----------------------------------------------------|
| `kurrentdb_cache_resources_bytes{cache=<CACHE_NAME>,kind=<"capacity"\|"size">}` | [Gauge](#common-types) | Current capacity/size of _CACHE_NAME_ cache in bytes |
| `kurrentdb_cache_resources_entries{cache=<CACHE_NAME>,kind="count"}`            | [Gauge](#common-types) | Current number of entries in _CACHE_NAME_ cache      |

Example configuration:
```json
"CacheResources": true
```

Example output:
```
# TYPE kurrentdb_cache_resources_bytes gauge
# UNIT kurrentdb_cache_resources_bytes bytes
kurrentdb_cache_resources_bytes{cache="LastEventNumber",kind="capacity"} 50000000 1688157491029
kurrentdb_cache_resources_bytes{cache="LastEventNumber",kind="size"} 15804 1688157491029

# TYPE kurrentdb_cache_resources_entries gauge
# UNIT kurrentdb_cache_resources_entries entries
kurrentdb_cache_resources_entries{cache="LastEventNumber",kind="count"} 75 1688157491029
```

### Checkpoints

| Time series                                                        | Type                   | Description                            |
|:-------------------------------------------------------------------|:-----------------------|:---------------------------------------|
| `kurrentdb_checkpoints{name=<CHECKPOINT_NAME>,read="non-flushed"}` | [Gauge](#common-types) | Value for _CHECKPOINT_NAME_ checkpoint |

Example configuration:
```json
"Checkpoints": {
  "Replication": true,
  "Chaser": false,
  "Epoch": false,
  "Index": false,
  "Proposal": false,
  "Truncate": false,
  "Writer": false,
  "StreamExistenceFilter": false
}
```

Example output:
```
# TYPE kurrentdb_checkpoints gauge
kurrentdb_checkpoints{name="replication",read="non-flushed"} 613363 1688054162478
```

### Elections Count

This metric tracks the number of elections that have been completed.

| Time series                       | Type                     | Description                  |
|:----------------------------------|:-------------------------|:-----------------------------|
| `kurrentdb_elections_count_total` | [Counter](#common-types) | Elections count in a cluster |

Example configuration:
```json
"ElectionsCount": true
```

Example output:
```
# TYPE kurrentdb_elections_count_total counter
kurrentdb_elections_count_total 0 1710188996949
```

### Events

These metrics track events written to and read from the server, including reads from caches.

| Time series                                                                                                      | Type                       | Description                                                                                          |
|:-----------------------------------------------------------------------------------------------------------------|:---------------------------|:-----------------------------------------------------------------------------------------------------|
| `kurrentdb_io_bytes_total{activity="read"}`                                                                      | [Counter](#common-types)   | Event bytes read                                                                                     |
| `kurrentdb_io_events_total{activity=<"read"\|"written">}`                                                        | [Counter](#common-types)   | Events read/written                                                                                  |
| `kurrentdb_logical_chunk_read_distribution_bucket{le=<CHUNK_OFFSET>}`                                            | [Histogram](#common-types) | Number of records read from chunks less than or equal to _CHUNK\_OFFSET_ older than the active chunk |
| `kurrentdb_io_record_read_duration_seconds_bucket{source=<"Archive"\|"ChunkCache"\|"FileSystem">,le=<DURATION>}` | [Histogram](#common-types) | Number of records read from the source with latency less than or equal to _DURATION_ in seconds      |

Example configuration:
```json
"Events": {
  "Read": false,
  "Written": true
}
```

Example output:
```
# TYPE kurrentdb_io_events_total counter
# UNIT kurrentdb_io_events_total events
kurrentdb_io_events_total{activity="written"} 320 1687963622074
```

`kurrentdb_logical_chunk_read_distribution_bucket` is a histogram showing where in the log events are being read from relative to the current tail.
This is useful for determining cutoff-points such as how much of the log should be kept in memory, and how much can be archived.
Note that the scale is exponential to accommodate a wide range of log sizes.

`kurrentdb_io_record_read_duration_seconds_bucket` is a histogram showing how long records are taking to be read from various sources (`Archive`, `ChunkCache`, `FileSystem`).
Note that any of these can return very quickly if the record happens to already be in the read buffer.

### Gossip

Measures the round trip latency and processing time of gossip.
Usually a node pushes new gossip to other nodes periodically or when its view of the cluster changes. Sometimes nodes pull gossip from each other if there is a suspected network problem. 

#### Gossip latency

| Time series                                                                                                        | Type                       | Description                                                                                  |
|:-------------------------------------------------------------------------------------------------------------------|:---------------------------|:---------------------------------------------------------------------------------------------|
| `kurrentdb_gossip_latency_seconds_bucket{activity="pull-from-peer",status=<"successful"\|"failed">,le=<DURATION>}` | [Histogram](#common-types) | Number of gossips pulled from peers with latency less than or equal to _DURATION_ in seconds |
| `kurrentdb_gossip_latency_seconds_bucket{activity="push-to-peer",status=<"successful"\|"failed">,le=<DURATION>}`   | [Histogram](#common-types) | Number of gossips pushed to peers with latency less than or equal to _DURATION_ in seconds   |

#### Gossip processing


| Time Series                                                                                                                                                   | Type                       | Description                                                                                                  |
|:--------------------------------------------------------------------------------------------------------------------------------------------------------------|:---------------------------|:-------------------------------------------------------------------------------------------------------------|
| `kurrentdb_gossip_processing_duration_seconds_bucket{`<br/>`activity="push-from-peer",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}`           | [Histogram](#common-types) | Number of gossips pushed from peers that took less than or equal to _DURATION_ in seconds to process         |
| `kurrentdb_gossip_processing_duration_seconds_bucket{`<br/>`activity="request-from-peer",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}`        | [Histogram](#common-types) | Number of gossip requests from peers that took less than or equal to _DURATION_ in seconds to process        |
| `kurrentdb_gossip_processing_duration_seconds_bucket{`<br/>`activity="request-from-grpc-client",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}` | [Histogram](#common-types) | Number of gossip requests from gRPC clients that took less than or equal to _DURATION_ in seconds to process |
| `kurrentdb_gossip_processing_duration_seconds_bucket{`<br/>`activity="request-from-http-client",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}` | [Histogram](#common-types) | Number of gossip requests from HTTP clients that took less than or equal to _DURATION_ in seconds to process |

Example configuration:
```json
"Gossip": {
  "PullFromPeer": false,
  "PushToPeer": true,
  "ProcessingPushFromPeer": false,
  "ProcessingRequestFromPeer": false,
  "ProcessingRequestFromGrpcClient": false,
  "ProcessingRequestFromHttpClient": false
}
```

Example output:
```
# TYPE kurrentdb_gossip_latency_seconds histogram
# UNIT kurrentdb_gossip_latency_seconds seconds
kurrentdb_gossip_latency_seconds_bucket{activity="push-to-peer",status="successful",le="0.005"} 8 1687972306948
```

### Incoming gRPC calls

| Time series                                                            | Type                     | Description                                                                                |
|:-----------------------------------------------------------------------|:-------------------------|:-------------------------------------------------------------------------------------------|
| `kurrentdb_current_incoming_grpc_calls`                                | [Gauge](#common-types)   | Inflight gRPC calls i.e. gRPC requests that have started on the server but not yet stopped |
| `kurrentdb_incoming_grpc_calls_total{kind="total"}`                    | [Counter](#common-types) | Total gRPC requests served                                                                 |
| `kurrentdb_incoming_grpc_calls_total{kind="failed"}`                   | [Counter](#common-types) | Total gRPC requests failed                                                                 |
| `kurrentdb_incoming_grpc_calls_total{`<br/>`kind="unimplemented"}`     | [Counter](#common-types) | Total gRPC requests made to unimplemented methods                                          |
| `kurrentdb_incoming_grpc_calls_total{`<br/>`kind="deadline-exceeded"}` | [Counter](#common-types) | Total gRPC requests for which deadline have exceeded                                       |

Example configuration:
```json
"IncomingGrpcCalls": {
  "Current": true,
  "Total": false,
  "Failed": true,
  "Unimplemented": false,
  "DeadlineExceeded": false
}
```

Example output:
```
# TYPE kurrentdb_current_incoming_grpc_calls gauge
kurrentdb_current_incoming_grpc_calls 1 1687963622074

# TYPE kurrentdb_incoming_grpc_calls_total counter
kurrentdb_incoming_grpc_calls_total{kind="failed"} 1 1687962877623
```

#### Client protocol gRPC methods

In addition, KurrentDB also records metrics for each of client protocol gRPC methods: `StreamRead`, `StreamAppend`, `StreamBatchAppend`, `StreamDelete` and `StreamTombstone`. They are grouped together according to the mapping defined in the configuration.

| Time series                                                                                                                        | Type                       | Description                                                                                      |
|:-----------------------------------------------------------------------------------------------------------------------------------|:---------------------------|:-------------------------------------------------------------------------------------------------|
| `kurrentdb_grpc_method_duration_seconds_bucket{`<br/>`activity=<LABEL>,`<br/>`status="successful"\|"failed",`<br/>`le=<DURATION>}` | [Histogram](#common-types) | Number of _LABEL_ gRPC requests that took less than or equal to _DURATION_ in seconds to process |

Example configuration:
```json
"GrpcMethods": {
  "StreamAppend": "append",
  "StreamBatchAppend": "append",
  // leaving label as blank will disable metric collection
  "StreamRead": "",
  "StreamDelete": "",
  "StreamTombstone": ""
}
```

Example output:
```
# TYPE kurrentdb_grpc_method_duration_seconds histogram
# UNIT kurrentdb_grpc_method_duration_seconds seconds
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="1E-06"} 0 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="1E-05"} 0 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.0001"} 129 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.001"} 143 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.01"} 168 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.1"} 169 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="1"} 169 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="10"} 169 1688157491029
kurrentdb_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="+Inf"} 169 1688157491029
```

### Kestrel

| Time series                     | Type                   | Description                        |
|:--------------------------------|:-----------------------|:-----------------------------------|
| `kurrentdb_kestrel_connections` | [Gauge](#common-types) | Number of open kestrel connections |

Example configuration:
```json
"Kestrel": {
  "ConnectionCount": true
}
```

Example output:
```
# TYPE kurrentdb_kestrel_connections gauge
kurrentdb_kestrel_connections 1 1688070655500
```

### Persistent Subscriptions

Persistent subscription metrics track the statistics for the persistent subscriptions.

| Time series                                                                                                                 | Type                     | Description                                                    |
|:----------------------------------------------------------------------------------------------------------------------------|:-------------------------|:---------------------------------------------------------------|
| `kurrentdb_persistent_sub_connections`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`                        | [Gauge](#common-types)   | Number of connections                                          |
| `kurrentdb_persistent_sub_parked_messages`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`                    | [Gauge](#common-types)   | Number of parked messages                                      |
| `kurrentdb_persistent_sub_in_flight_messages`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`                 | [Gauge](#common-types)   | Number of messages in flight                                   |
| `kurrentdb_persistent_sub_oldest_parked_message_seconds`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`      | [Gauge](#common-types)   | Oldest parked message age in seconds                           |
| `kurrentdb_persistent_sub_items_processed_total`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`              | [Counter](#common-types) | Total items processed                                          |
| `kurrentdb_persistent_sub_last_known_event_number`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`            | [Gauge](#common-types)   | Last known event number (streams other than `$all`)            |
| `kurrentdb_persistent_sub_last_known_event_commit_position`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`   | [Gauge](#common-types)   | Last known event's commit position (`$all` stream only)        |
| `kurrentdb_persistent_sub_checkpointed_event_number`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`          | [Gauge](#common-types)   | Last checkpointed event number (streams other than `$all)`     |
| `kurrentdb_persistent_sub_checkpointed_event_commit_position`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}` | [Gauge](#common-types)   | Last checkpointed event's commit position (`$all` stream only) |

Example configuration:

```json
"PersistentSubscriptionStats": true,
```

Example output:

```
# TYPE kurrentdb_persistent_sub_connections gauge
kurrentdb_persistent_sub_connections{event_stream_id="test-stream",group_name="group1"} 1 1720172078179
kurrentdb_persistent_sub_connections{event_stream_id="$all",group_name="group1"} 1 1720172078179

# TYPE kurrentdb_persistent_sub_parked_messages gauge
kurrentdb_persistent_sub_parked_messages{event_stream_id="test-stream",group_name="group1"} 0 1720172078179
kurrentdb_persistent_sub_parked_messages{event_stream_id="$all",group_name="group1"} 0 1720172078179

# TYPE kurrentdb_persistent_sub_in_flight_messages gauge
kurrentdb_persistent_sub_in_flight_messages{event_stream_id="test-stream",group_name="group1"} 0 1720172078179
kurrentdb_persistent_sub_in_flight_messages{event_stream_id="$all",group_name="group1"} 0 1720172078179

# TYPE kurrentdb_persistent_sub_oldest_parked_message_seconds gauge
kurrentdb_persistent_sub_oldest_parked_message_seconds{event_stream_id="test-stream",group_name="group1"} 0 1720172078179
kurrentdb_persistent_sub_oldest_parked_message_seconds{event_stream_id="$all",group_name="group1"} 0 1720172078179

# TYPE kurrentdb_persistent_sub_items_processed_total counter
kurrentdb_persistent_sub_items_processed_total{event_stream_id="test-stream",group_name="group1"} 0 1720172078179
kurrentdb_persistent_sub_items_processed_total{event_stream_id="$all",group_name="group1"} 5 1720172078179

# TYPE kurrentdb_persistent_sub_last_known_event_number gauge
kurrentdb_persistent_sub_last_known_event_number{event_stream_id="test-stream",group_name="group1"} 0 1720172078179

# TYPE kurrentdb_persistent_sub_last_known_event_commit_position gauge
kurrentdb_persistent_sub_last_known_event_commit_position{event_stream_id="$all",group_name="group1"} 4113 1720172078179

# TYPE kurrentdb_persistent_sub_checkpointed_event_number gauge
kurrentdb_persistent_sub_checkpointed_event_number{event_stream_id="test-stream",group_name="group1"} 0 1720172078179

# TYPE kurrentdb_persistent_sub_checkpointed_event_commit_position gauge
kurrentdb_persistent_sub_checkpointed_event_commit_position{event_stream_id="$all",group_name="group1"} 4013 1720172078179
```

### Process

KurrentDB collects key metrics about the running process.

| Time Series                                                                             | Type                     | Description                                                                                                                                                                                                                                                                                                                                                                          |
|:----------------------------------------------------------------------------------------|:-------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `kurrentdb_proc_up_time_seconds_total{pid=<PID>}`                                       | [Counter](#common-types) | Time in seconds this process has been running for. _PID_ is process Id of KurrentDB process                                                                                                                                                                                                                                                                                          |
| `kurrentdb_proc_cpu`                                                                    | [Gauge](#common-types)   | Process CPU usage                                                                                                                                                                                                                                                                                                                                                                    |
| `kurrentdb_proc_thread_count`                                                           | [Gauge](#common-types)   | Current number of threadpool threads ([ThreadPool.ThreadCount](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.threadcount?view=net-6.0))                                                                                                                                                                                                                   |
| `kurrentdb_proc_thread_pool_pending_work_item_count`                                    | [Gauge](#common-types)   | Current number of items that are queued to be processed by threadpool threads ([ThreadPool.PendingWorkItemCount](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.pendingworkitemcount?view=net-6.0))                                                                                                                                                        |
| `kurrentdb_proc_contention_count_total`                                                 | [Counter](#common-types) | Total number of times there was contention when trying to take monitor's lock ([Monitor.LockContentionCount](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.lockcontentioncount?view=net-6.0))                                                                                                                                                                |
| `kurrentdb_proc_exception_count_total`                                                  | [Counter](#common-types) | Total number of exceptions thrown                                                                                                                                                                                                                                                                                                                                                    |
| `kurrentdb_gc_time_in_gc`                                                               | [Gauge](#common-types)   | Percentage of CPU time spent collecting garbage during last garbage collection                                                                                                                                                                                                                                                                                                       |
| `kurrentdb_gc_heap_size_bytes`                                                          | [Gauge](#common-types)   | [Heap size in bytes](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.heapsizebytes?view=net-6.0)                                                                                                                                                                                                                                                                    |
| `kurrentdb_gc_heap_fragmentation`                                                       | [Gauge](#common-types)   | Percentage of heap [fragmentation](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.fragmentedbytes?view=net-6.0) during last garbage collection                                                                                                                                                                                                                     |
| `kurrentdb_gc_allocated_bytes_total`                                                    | [Counter](#common-types) | [Total allocated bytes](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-6.0) over the lifetime of this process                                                                                                                                                                                                                                |
| `kurrentdb_gc_pause_duration_max_seconds{`<br/>`range=<RANGE>}`                         | [RecentMax](#recentmax)  | Recent maximum garbage collection pause in seconds. This measures the times that the execution engine is paused for GC                                                                                                                                                                                                                                                               |
| `kurrentdb_gc_generation_size_bytes{`<br/>`generation=<"gen0"\|"gen1"\|"gen2"\|"loh">}` | [Gauge](#common-types)   | Size of each generation in bytes                                                                                                                                                                                                                                                                                                                                                     |
| `kurrentdb_gc_collection_count_total{`<br/>`generation=<"gen0"\|"gen1"\|"gen2">}`       | [Counter](#common-types) | [Number of garbage collections](https://learn.microsoft.com/en-us/dotnet/api/system.gc.collectioncount?view=net-6.0) from each generation                                                                                                                                                                                                                                            |
| `kurrentdb_proc_mem_bytes{`<br/>`kind=<"working-set"\|"paged-bytes"\|"virtual-bytes">}` | [Gauge](#common-types)   | Size in bytes of the [working set](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64?view=net-6.0), [paged](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.pagedmemorysize64?view=net-6.0) or [virtual](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.virtualmemorysize64?view=net-6.0) memory |
| `kurrentdb_disk_io_bytes_total{`<br/>`activity=<"read"\|"written">}`                    | [Counter](#common-types) | Number of bytes read from/written to the disk                                                                                                                                                                                                                                                                                                                                        |
| `kurrentdb_disk_io_operations_total{`<br/>`activity=<"read"\|"written">}`               | [Counter](#common-types) | Number of OS read/write operations issued to the disk                                                                                                                                                                                                                                                                                                                                |

Example configuration:
```json
"Process": {
  "UpTime": false,
  "Cpu": false,
  "MemWorkingSet": false,
  "MemPagedBytes": false,
  "MemVirtualBytes": false,
  "ThreadCount": true,
  "ThreadPoolPendingWorkItemCount": false,
  "LockContentionCount": true,
  "ExceptionCount": false,
  "Gen0CollectionCount": false,
  "Gen1CollectionCount": false,
  "Gen2CollectionCount": false,
  "Gen0Size": false,
  "Gen1Size": false,
  "Gen2Size": false,
  "LohSize": false,
  "TimeInGc": false,
  "GcPauseDuration": true,
  "HeapSize": false,
  "HeapFragmentation": false,
  "TotalAllocatedBytes": false,
  "DiskReadBytes": false,
  "DiskReadOps": false,
  "DiskWrittenBytes": false,
  "DiskWrittenOps": false
}
```

Example output:
```
# TYPE kurrentdb_proc_thread_count gauge
kurrentdb_proc_thread_count 15 1688070655500

# TYPE kurrentdb_proc_contention_count_total counter
kurrentdb_proc_contention_count_total 297 1688147136862

# TYPE kurrentdb_gc_pause_duration_max_seconds gauge
# UNIT kurrentdb_gc_pause_duration_max_seconds seconds
kurrentdb_gc_pause_duration_max_seconds{range="16-20 seconds"} 0.0485873 1688147136862
```

### Projections

Projection metrics track the statistics for projections.

| Time series                                                                                      | Type                     | Description                                                      |
|:-------------------------------------------------------------------------------------------------|:-------------------------|:-----------------------------------------------------------------|
| `kurrentdb_projection_events_processed_after_restart_total`<br/>`{projection=<PROJECTION_NAME>}` | [Counter](#common-types) | Projection event processed count after restart                   |
| `kurrentdb_projection_progress{projection=<PROJECTION_NAME>}`                                    | [Gauge](#common-types)   | Projection progress 0 - 1, where 1 = projection progress at 100% |
| `kurrentdb_projection_running{projection=<PROJECTION_NAME>}`                                     | [Gauge](#common-types)   | If 1, projection is in 'Running' state                           |
| `kurrentdb_projection_status{projection=<PROJECTION_NAME>,status=<PROJECTION_STATUS>}`           | [Gauge](#common-types)   | If 1, projection is in specified state                           |

`Status` can have one of the following statuses:

- Running
- Faulted
- Stopped

Example configuration:

```json
"ProjectionStats": true
```

Example output:

```
# TYPE kurrentdb_projection_events_processed_after_restart_total counter
kurrentdb_projection_events_processed_after_restart_total{projection="$by_category"} 83 1719526306309

# TYPE kurrentdb_projection_progress gauge
kurrentdb_projection_progress{projection="$stream_by_category"} 1 1719526306309

# TYPE kurrentdb_projection_running gauge
kurrentdb_projection_running{projection="$by_category"} 1 1719526306309

# TYPE kurrentdb_projection_status gauge
kurrentdb_projection_status{projection="$by_category",status="Running"} 1 1719526306309
kurrentdb_projection_status{projection="$by_category",status="Faulted"} 0 1719526306309
kurrentdb_projection_status{projection="$by_category",status="Stopped"} 0 1719526306309
```

### Queues

KurrentDB uses various queues for asynchronous processing for which it also collects different metrics. In addition, KurrentDB allows users to group queues and monitor them as a unit.

| Time series                                                                                                        | Type                       | Description                                                                                                                                                                                      |
|:-------------------------------------------------------------------------------------------------------------------|:---------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `kurrentdb_queue_busy_seconds_total{queue=<QUEUE_GROUP>}`                                                          | [Counter](#common-types)   | Total time spent processing in seconds, averaged across the queues in the _QUEUE_GROUP_. The rate of this metric is therefore the average busyness of the group during the period (from 0-1 s/s) |
| `kurrentdb_queue_queueing_duration_max_seconds{`<br/>`name=<QUEUE_GROUP>,range=<RANGE>}`                           | [RecentMax](#recentmax)    | Recent maximum time in seconds for which any item was queued in queues belonging to the _QUEUE_GROUP_. This is essentially the length of the longest queue in the group in seconds               |
| `kurrentdb_queue_processing_duration_seconds_bucket{`<br/>`message_type=<TYPE>,queue=<QUEUE_GROUP>,le=<DURATION>}` | [Histogram](#common-types) | Number of messages of type _TYPE_ processed by _QUEUE_GROUP_ group that took less than or equal to _DURATION_ in seconds                                                                         |

`QueueLabels` setting within `metricsconfig.json` can be used to group queues, based on regex which gets matched on queue names, and label them for metrics reporting. Capture groups are also supported. Message types can be grouped in the same way in the `MessageTypes` setting in `metricsconfig.json`.

::: note
Enabling `Queues.Processing` can cause a lot more time series to be generated, according to the `QueueLabels` and `MessageTypes` configuration.
:::

Example configuration:
```json
"Queues": {
  "Busy": true,
  "Length": true,
  "Processing": false
}

"QueueLabels": [
  {
    "Regex": "StorageReaderQueue #.*",
    "Label": "Readers"
  },
  {
    "Regex": ".*",
    "Label": "Others"
  }
]
```

Example output:
```
# TYPE kurrentdb_queue_busy_seconds_total counter
# UNIT kurrentdb_queue_busy_seconds_total seconds
kurrentdb_queue_busy_seconds_total{queue="Readers"} 1.04568158125 1688157491029
kurrentdb_queue_busy_seconds_total{queue="Others"} 0 1688157491029

# TYPE kurrentdb_queue_queueing_duration_max_seconds gauge
# UNIT kurrentdb_queue_queueing_duration_max_seconds seconds
kurrentdb_queue_queueing_duration_max_seconds{name="Readers",range="16-20 seconds"} 0.06434454 1688157489545
kurrentdb_queue_queueing_duration_max_seconds{name="Others",range="16-20 seconds"} 0 1688157489545
```

### Status

KurrentDB tracks the current status of the `Node` role  as well as  progress of  `Index`, and `Scavenge` processes.

| Time series                                       | Type                   | Description                                                                              |
|:--------------------------------------------------|:-----------------------|:-----------------------------------------------------------------------------------------|
| `kurrentdb_statuses{name=<NAME>,status=<STATUS>}` | [Gauge](#common-types) | Number of seconds since the 1970 epoch when _NAME_ most recently had the status _STATUS_ |

For a given _NAME_, the current status can be determined by taking the max of all the time series with that name.

`Index` can have one of the following statuses:
- Opening (loading/verifying the PTables)
- Rebuilding (indexing previously written records on start up)
- Initializing (initializing any other parts of the index e.g. StreamExistenceFilter on start up)
- Merging
- Scavenging
- Idle

`Scavenge` can have one of the following statuses:
- Accumulation
- Calculation
- Chunk Execution
- Chunk Merging
- Index Execution
- Cleaning
- Idle

`Node` can be one of the [node roles](../configuration/cluster.md#node-roles).

Example configuration:
```json
"Statuses": {
  "Index": true,
  "Node": false,
  "Scavenge": false
}
```

Example output:
```
# TYPE kurrentdb_checkpoints gauge
kurrentdb_statuses{name="Index",status="Idle"} 1688054162 1688054162477
```

### Storage Writer

| Time series                                                  | Type                    | Description                              |
|:-------------------------------------------------------------|:------------------------|:-----------------------------------------|
| `kurrentdb_writer_flush_size_max{range=<RANGE>}`             | [RecentMax](#recentmax) | Recent maximum flush size in bytes       |
| `kurrentdb_writer_flush_duration_max_seconds{range=<RANGE>}` | [RecentMax](#recentmax) | Recent maximum flush duration in seconds |

Example configuration:
```json
"Writer": {
  "FlushSize": true,
  "FlushDuration": false
}
```

Example output:
```
# TYPE kurrentdb_writer_flush_size_max gauge
kurrentdb_writer_flush_size_max{range="16-20 seconds"} 410 1688056823193
```

### System

| Time series                                                           | Type                   | Description                                                                                                 |
|:----------------------------------------------------------------------|:-----------------------|:------------------------------------------------------------------------------------------------------------|
| `kurrentdb_sys_load_avg{period=<"1m"\|"5m"\|"15m">}`                  | [Gauge](#common-types) | Average system load in last `1`, `5`, and `15` minutes. This metric is only available for Unix-like systems |
| `kurrentdb_sys_cpu`                                                   | [Gauge](#common-types) | Current CPU usage in percentage. This metric is unavailable for Unix-like systems                           |
| `kurrentdb_sys_mem_bytes{kind=<"free"\|"total">}`                     | [Gauge](#common-types) | Current free/total memory in bytes                                                                          |
| `kurrentdb_sys_disk_bytes{disk=<MOUNT_POINT>,kind=<"used"\|"total">}` | [Gauge](#common-types) | Current used/total bytes of disk mounted at _MOUNT_POINT_                                                   |

Example configuration:
```json
"System": {
  "Cpu": false,
  "LoadAverage1m": false,
  "LoadAverage5m": false,
  "LoadAverage15m": false,
  "FreeMem": false,
  "TotalMem": false,
  "DriveTotalBytes": false,
  "DriveUsedBytes": true
}
```

Example output:
```
# TYPE kurrentdb_sys_disk_bytes gauge
# UNIT kurrentdb_sys_disk_bytes bytes
kurrentdb_sys_disk_bytes{disk="/home",kind="used"} 38947205120 1688070655500
```

## Metric types

### Common types

Please refer to [Prometheus documentation](https://prometheus.io/docs/concepts/metric_types/) for explanation of common metric types (`Gauge`, `Counter` and `Histogram`).

### RecentMax

A gauge whose value represents the maximum out of a set of _recent_ measurements. Its purpose is to capture spikes that would otherwise have fallen in-between scrapes.

::: note
The `ExpectedScrapeIntervalSeconds` setting within `metricsconfig.json` can be used to control the size of the window that the max is calculated over. It represents the expected interval between scrapes by a consumer such as Prometheus. It can only take specific values: `0`, `1`, `5`, `10` or multiples of `15`.

Setting the expected scape interval correctly ensures that spikes in the time series will be captured by at least one scrape and at most two.
:::

Example output:
Following metric is reported when `ExpectedScrapeIntervalSeconds` is set to `15` seconds
```
# TYPE kurrentdb_writer_flush_size_max gauge
kurrentdb_writer_flush_size_max{range="16-20 seconds"} 1854 1688070655500
```
In above example, maximum reported is `1854`. It is not a maximum measurement in last `15s` but rather maximum measurement in last `16` to last `20` seconds i.e. the maximum measurement could have been recorded in last `16s`, last `17s`, …, upto last `20s`.

