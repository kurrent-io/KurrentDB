---
order: 2
---

# Metrics

EventStoreDB collects metrics in [prometheus format](https://prometheus.io/docs/instrumenting/exposition_formats/#text-based-format), available on the `/metrics` endpoint. Prometheus can be configured to scrape this endpoint directly. The metrics are configured in `metricsconfig.json`. 

::: note
Native EventStoreDB metrics do not yet contain all of the metrics for Projections and Persistent Subscriptions. To view these in Prometheus you can still use the [Prometheus exporter](https://github.com/marcinbudny/eventstore_exporter).
:::

## Metrics Reference

### Caches

#### Cache hits and misses

EventStoreDB tracks cache hits/misses metrics for `stream-info` and `chunk` caches.

| Time Series                                                                | Type                     | Description                             |
|:---------------------------------------------------------------------------|:-------------------------|:----------------------------------------|
| `eventstore_cache_hits_misses{cache=<CACHE_NAME>,kind=<"hits"\|"misses">}` | [Counter](#common-types) | Total hits/misses on _CACHE_NAME_ cache |

Example Configuration:
```json
"CacheHitsMisses": {
	"StreamInfo": true,
	"Chunk": false
}
```

Example Output:
```
# TYPE eventstore_cache_hits_misses counter
eventstore_cache_hits_misses{cache="stream-info",kind="hits"} 104329 1688157489545
eventstore_cache_hits_misses{cache="stream-info",kind="misses"} 117 1688157489545
```

#### Dynamic Cache Resources

Certain caches that EventStoreDB uses are dynamic in nature i.e. their capacity scales up/down during their lifetime. EventStoreDB records metrics for resources being used by each such dynamic cache.

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_cache_resources_bytes{cache=<CACHE_NAME>,kind=<"capacity"\|"size">}` | [Gauge](#common-types) | Current capacity/size of _CACHE_NAME_ cache in bytes |
| `eventstore_cache_resources_entries{cache=<CACHE_NAME>,kind="count"}`            | [Gauge](#common-types) | Current number of entries in _CACHE_NAME_ cache |

Example Configuration:
```
"CacheResources": true
```

Example Output:
```
# TYPE eventstore_cache_resources_bytes gauge
# UNIT eventstore_cache_resources_bytes bytes
eventstore_cache_resources_bytes{cache="LastEventNumber",kind="capacity"} 50000000 1688157491029
eventstore_cache_resources_bytes{cache="LastEventNumber",kind="size"} 15804 1688157491029

# TYPE eventstore_cache_resources_entries gauge
# UNIT eventstore_cache_resources_entries entries
eventstore_cache_resources_entries{cache="LastEventNumber",kind="count"} 75 1688157491029
```

### Checkpoints

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_checkpoints{name=<CHECKPOINT_NAME>,read="non-flushed"}` | [Gauge](#common-types) | Value for _CHECKPOINT_NAME_ checkpoint|

Example Configuration:
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

Example Output:
```
# TYPE eventstore_checkpoints gauge
eventstore_checkpoints{name="replication",read="non-flushed"} 613363 1688054162478
```

### Events

These metrics track events written to and read from the server, including reads from caches.

| Time Series                                          | Type                     | Description         |
|:-----------------------------------------------------|:-------------------------|:--------------------|
| `eventstore_io_bytes{activity="read"}`               | [Counter](#common-types) | Event bytes read    |
| `eventstore_io_events{activity=<"read"\|"written">}` | [Counter](#common-types) | Events read/written |

Example Configuration:
```json
"Events": {
	"Read": false,
	"Written": true
}
```

Example Output:
```
# TYPE eventstore_io_events counter
# UNIT eventstore_io_events events
eventstore_io_events{activity="written"} 320 1687963622074
```

### Gossip

Measures the round trip latency and processing time of gossip.
Usually a node pushes new gossip to other nodes periodically or when its view of the cluster changes. Sometimes nodes pull gossip from each other if there is a suspected network problem. 

#### Gossip Latency

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_gossip_latency_seconds_bucket{activity="pull-from-peer",status=<"successful"\|"failed">,le=<DURATION>}`                       | [Histogram](#common-types) | Number of gossips pulled from peers with latency less than or equal to _DURATION_ in seconds                         |
| `eventstore_gossip_latency_seconds_bucket{activity="push-to-peer",status=<"successful"\|"failed">,le=<DURATION>}`                         | [Histogram](#common-types) | Number of gossips pushed to peers with latency less than or equal to _DURATION_ in seconds                      |

#### Gossip Processing

| Time Series                                                                                                                                                    | Type                       | Description                                                                                                  |
|:---------------------------------------------------------------------------------------------------------------------------------------------------------------|:---------------------------|:-------------------------------------------------------------------------------------------------------------|
| `eventstore_gossip_processing_duration_seconds_bucket{`<br/>`activity="push-from-peer",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}`           | [Histogram](#common-types) | Number of gossips pushed from peers that took less than or equal to _DURATION_ in seconds to process         |
| `eventstore_gossip_processing_duration_seconds_bucket{`<br/>`activity="request-from-peer",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}`        | [Histogram](#common-types) | Number of gossip requests from peers that took less than or equal to _DURATION_ in seconds to process        |
| `eventstore_gossip_processing_duration_seconds_bucket{`<br/>`activity="request-from-grpc-client",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}` | [Histogram](#common-types) | Number of gossip requests from gRPC clients that took less than or equal to _DURATION_ in seconds to process |
| `eventstore_gossip_processing_duration_seconds_bucket{`<br/>`activity="request-from-http-client",`<br/>`status=<"successful"\|"failed">,`<br/>`le=<DURATION>}` | [Histogram](#common-types) | Number of gossip requests from HTTP clients that took less than or equal to _DURATION_ in seconds to process |

Example Configuration:
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

Example Output:
```
# TYPE eventstore_gossip_latency_seconds histogram
# UNIT eventstore_gossip_latency_seconds seconds
eventstore_gossip_latency_seconds_bucket{activity="push-to-peer",status="successful",le="0.005"} 8 1687972306948
```

### Incoming Grpc Calls

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_current_incoming_grpc_calls`                   | [Gauge](#common-types) | Inflight gRPC calls i.e. gRPC requests that have started on the server but not yet stopped |
| `eventstore_incoming_grpc_calls{kind="total"}`             | [Counter](#common-types) | Total gRPC requests served                           |
| `eventstore_incoming_grpc_calls{kind="failed"}`            | [Counter](#common-types) | Total gRPC requests failed                           |
| `eventstore_incoming_grpc_calls{kind="unimplemented"}`     | [Counter](#common-types) | Total gRPC requests made to unimplemented methods    |
| `eventstore_incoming_grpc_calls{kind="deadline-exceeded"}` | [Counter](#common-types) | Total gRPC requests for which deadline have exceeded |

Example Configuration:
```json
"IncomingGrpcCalls": {
	"Current": true,
	"Total": false,
	"Failed": true,
	"Unimplemented": false,
	"DeadlineExceeded": false
}
```

Example Output:
```
# TYPE eventstore_current_incoming_grpc_calls gauge
eventstore_current_incoming_grpc_calls 1 1687963622074

# TYPE eventstore_incoming_grpc_calls counter
eventstore_incoming_grpc_calls{kind="failed"} 1 1687962877623
```

#### Client protocol gRPC methods

In addition, EventStoreDB also records metrics for each of client protocol gRPC methods : `StreamRead`, `StreamAppend`, `StreamBatchAppend`, `StreamDelete` and `StreamTombstone`. They are grouped together according to the mapping defined in the configuration.

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_grpc_method_duration_seconds_bucket{activity=<LABEL>,status="successful"\|"failed",le=<DURATION>}` | [Histogram](#common-types) | Number of _LABEL_ gRPC requests that took less than or equal to _DURATION_ in seconds to process |

Example Configuration:
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

Example Output:
```
# TYPE eventstore_grpc_method_duration_seconds histogram
# UNIT eventstore_grpc_method_duration_seconds seconds
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="1E-06"} 0 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="1E-05"} 0 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.0001"} 129 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.001"} 143 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.01"} 168 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="0.1"} 169 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="1"} 169 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="10"} 169 1688157491029
eventstore_grpc_method_duration_seconds_bucket{activity="append",status="successful",le="+Inf"} 169 1688157491029
```

### Kestrel

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_kestrel_connections`  | [Gauge](#common-types) | Number of open kestrel connections |

Example Configuration:
```json
"Kestrel": {
	"ConnectionCount": true
}
```

Example Output:
```
# TYPE eventstore_kestrel_connections gauge
eventstore_kestrel_connections 1 1688070655500
```

### Persistent Subscriptions

Persistent subscription metrics track the statistics for the persistent subscriptions.

| Time series                                                                                                                 | Type                     | Description                                                    |
|:----------------------------------------------------------------------------------------------------------------------------|:-------------------------|:---------------------------------------------------------------|
| `eventstore_persistent_subscriptions_messages_parked`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>,reason=<REASON>}` | [Counter](#common-types) | Count of Park Message requests. Reason can be `client-nak` or `max-retries` |
| `eventstore_persistent_subscriptions_replays_requested`<br/>`{event_stream_id=<STREAM_NAME>,group_name=<GROUP_NAME>}`                | [Counter](#common-types) | Count of Parked Message replay requests                                     |

::: warning
The `eventstore_persistent_subscriptions_messages_parked` and `eventstore_persistent_subscriptions_replays_requested` metrics have been renamed to `eventstore_persistent_sub_park_message_requests` and `eventstore_persistent_sub_parked_message_replays` respectively in EventStoreDB version 24.10 and onwards.
:::

Example configuration:

```json
"PersistentSubscriptionStats": true,
```

Example Output:
```
# TYPE eventstore_persistent_subscriptions_messages_parked counter
eventstore_persistent_subscriptions_messages_parked{reason="client-nak"} 0 1750337191056
eventstore_persistent_subscriptions_messages_parked{reason="max-retries"} 0 1750337191056

# TYPE eventstore_persistent_subscriptions_replays_requested counter
eventstore_persistent_subscriptions_replays_requested 0 1750337191056
```

::: note
Native EventStoreDB metrics do not yet contain all of the metrics for Persistent Subscriptions. To view these in Prometheus you can still use the [Prometheus exporter](https://github.com/marcinbudny/eventstore_exporter).
:::

### Process

EventStoreDB collects key metrics about the running process.

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_proc_up_time{pid=<PID>}`                                              | [Counter](#common-types) | Time in seconds this process has been running for. _PID_ is process Id of EventStoreDB process |
| `eventstore_proc_cpu`                                                             | [Gauge](#common-types)   | Process CPU usage |
| `eventstore_proc_thread_count`                                                    | [Gauge](#common-types)   | Current number of threadpool threads ([ThreadPool.ThreadCount](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.threadcount?view=net-6.0))  |
| `eventstore_proc_thread_pool_pending_work_item_count`                             | [Gauge](#common-types)   | Current number of items that are queued to be processed by threadpool threads ([ThreadPool.PendingWorkItemCount](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.pendingworkitemcount?view=net-6.0)) |
| `eventstore_proc_contention_count`                                                | [Counter](#common-types) | Total number of times there was contention when trying to take monitor's lock ([Monitor.LockContentionCount](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.lockcontentioncount?view=net-6.0))  |
| `eventstore_proc_exception_count`                                                 | [Counter](#common-types) | Total number of exceptions thrown  |
| `eventstore_gc_time_in_gc`                                                        | [Gauge](#common-types)   | Percentage of CPU time spent collecting garbage during last garbage collection  |
| `eventstore_gc_heap_size_bytes`                                                   | [Gauge](#common-types)   | [Heap size in bytes](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.heapsizebytes?view=net-6.0)  |
| `eventstore_gc_heap_fragmentation`                                                | [Gauge](#common-types)   | Percentage of heap [fragmentation](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.fragmentedbytes?view=net-6.0) during last garbage collection  |
| `eventstore_gc_total_allocated`                                                   | [Counter](#common-types) | [Total allocated bytes](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-6.0) over the lifetime of this process  |
| `eventstore_gc_pause_duration_max_seconds{range=<RANGE>}`                         | [RecentMax](#recentmax)  | Recent maximum garbage collection pause in seconds. This measures the times that the execution engine is paused for GC  |
| `eventstore_gc_generation_size_bytes{generation=<"gen0"\|"gen1"\|"gen2"\|"loh">}` | [Gauge](#common-types)   | Size of each generation in bytes  |
| `eventstore_gc_collection_count{generation=<"gen0"\|"gen1"\|"gen2">}`             | [Counter](#common-types) | [Number of garbage collections](https://learn.microsoft.com/en-us/dotnet/api/system.gc.collectioncount?view=net-6.0) from each generation  |
| `eventstore_proc_mem_bytes{kind=<"working-set"\|"paged-bytes"\|"virtual-bytes">}` | [Gauge](#common-types)   | Size in bytes of the [working set](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64?view=net-6.0), [paged](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.pagedmemorysize64?view=net-6.0) or [virtual](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.virtualmemorysize64?view=net-6.0) memory |
| `eventstore_disk_io_bytes{activity=<"read"\|"written">}`                          | [Counter](#common-types) | Number of bytes read from/written to the disk  |
| `eventstore_disk_io_operations{activity=<"read"\|"written">}`                     | [Counter](#common-types) | Number of OS read/write operations issued to the disk  |

Example Configuration:
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

Example Output:
```
# TYPE eventstore_proc_thread_count gauge
eventstore_proc_thread_count 15 1688070655500

# TYPE eventstore_proc_contention_count counter
eventstore_proc_contention_count 297 1688147136862

# TYPE eventstore_gc_pause_duration_max_seconds gauge
# UNIT eventstore_gc_pause_duration_max_seconds seconds
eventstore_gc_pause_duration_max_seconds{range="16-20 seconds"} 0.0485873 1688147136862
```

### Queues

EventStoreDB uses various queues for asynchronous processing for which it also collects different metrics. In addition, EventStoreDB allows users to group queues and monitor them as a unit.

| Time Series                                                                                                         | Type                       | Description                                                                                                                                                                                      |
|:--------------------------------------------------------------------------------------------------------------------|:---------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `eventstore_queue_busy_seconds{queue=<QUEUE_GROUP>}`                                                                | [Counter](#common-types)   | Total time spent processing in seconds, averaged across the queues in the _QUEUE_GROUP_. The rate of this metric is therefore the average busyness of the group during the period (from 0-1 s/s) |
| `eventstore_queue_queueing_duration_max_seconds`<br/>`{name=<QUEUE_GROUP>,range=<RANGE>}`                           | [RecentMax](#recentmax)    | Recent maximum time in seconds for which any item was queued in queues belonging to the _QUEUE_GROUP_. This is essentially the length of the longest queue in the group in seconds               |
| `eventstore_queue_processing_duration_seconds_bucket`<br/>`{message_type=<TYPE>,queue=<QUEUE_GROUP>,le=<DURATION>}` | [Histogram](#common-types) | Number of messages of type _TYPE_ processed by _QUEUE_GROUP_ group that took less than or equal to _DURATION_ in seconds                                                                         |

`QueueLabels` setting within `metricsconfig.json` can be used to group queues, based on regex which gets matched on queue names, and label them for metrics reporting. Capture groups are also supported. Message types can be grouped in the same way in the `MessageTypes` setting in `metricsconfig.json`.

::: note
Enabling `Queues.Processing` can cause a lot more time series to be generated, according to the `QueueLabels` and `MessageTypes` configuration.
:::

Example Configuration:
```json
"Queues": {
	"Busy": true,
	"Length": true,
	"Processing": false
},
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

Example Output:
```
# TYPE eventstore_queue_busy_seconds counter
# UNIT eventstore_queue_busy_seconds seconds
eventstore_queue_busy_seconds{queue="Readers"} 1.04568158125 1688157491029
eventstore_queue_busy_seconds{queue="Others"} 0 1688157491029

# TYPE eventstore_queue_queueing_duration_max_seconds gauge
# UNIT eventstore_queue_queueing_duration_max_seconds seconds
eventstore_queue_queueing_duration_max_seconds{name="Readers",range="16-20 seconds"} 0.06434454 1688157489545
eventstore_queue_queueing_duration_max_seconds{name="Others",range="16-20 seconds"} 0 1688157489545
```

### Status

EventStoreDB tracks the current status of the `Node` role  as well as  progress of  `Index`, and `Scavenge` processes.

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_statuses{name=<NAME>,status=<STATUS>}` | [Gauge](#common-types) | Number of seconds since the 1970 epoch when _NAME_ most recently had the status _STATUS_ |

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

Example Configuration:
```json
"Statuses": {
	"Index": true,
	"Node": false,
	"Scavenge": false
}
```

Example Output:
```
# TYPE eventstore_checkpoints gauge
eventstore_statuses{name="Index",status="Idle"} 1688054162 1688054162477
```

### Storage Writer

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_writer_flush_size_max{range=<RANGE>}`             | [RecentMax](#recentmax) | Recent maximum flush size in bytes       |
| `eventstore_writer_flush_duration_max_seconds{range=<RANGE>}` | [RecentMax](#recentmax) | Recent maximum flush duration in seconds |

Example Configuration:
```
"Writer": {
	"FlushSize": true,
	"FlushDuration": false
}
```

Example Output:
```
# TYPE eventstore_writer_flush_size_max gauge
eventstore_writer_flush_size_max{range="16-20 seconds"} 410 1688056823193
```

### System

| Time Series | Type       | Description |
| :---------- | :--------- | :---------- |
| `eventstore_sys_load_avg{period=<"1m"\|"5m"\|"15m">}`                  | [Gauge](#common-types) | Average system load in last `1`, `5`, and `15` minutes. This metric is only available for Unix-like systems |
| `eventstore_sys_cpu`                                                   | [Gauge](#common-types) | Current CPU usage in percentage. This metric is unavailable for Unix-like systems |
| `eventstore_sys_mem_bytes{kind=<"free"\|"total">}`                     | [Gauge](#common-types) | Current free/total memory in bytes |
| `eventstore_sys_disk_bytes{disk=<MOUNT_POINT>,kind=<"used"\|"total">}` | [Gauge](#common-types) | Current used/total bytes of disk mounted at _MOUNT_POINT_ |

Example Configuration:
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

Example Output:
```
# TYPE eventstore_sys_disk_bytes gauge
# UNIT eventstore_sys_disk_bytes bytes
eventstore_sys_disk_bytes{disk="/home",kind="used"} 38947205120 1688070655500
```

## Metric Types

### Common types

Please refer to [Prometheus documentation](https://prometheus.io/docs/concepts/metric_types/) for explanation of common metric types (`Gauge`, `Counter` and `Histogram`).

### RecentMax
A gauge whose value represents the maximum out of a set of _recent_ measurements. It's purpose is to capture spikes that would otherwise have fallen in-between scrapes.

::: note
The `ExpectedScrapeIntervalSeconds` setting within `metricsconfig.json` can be used to control the size of the window that the max is calculated over. It represents the expected interval between scrapes by a consumer such as Prometheus. It can only take specific values: `0`, `1`, `5`, `10` or multiples of `15`.

Setting the expected scape interval correctly ensures that spikes in the time series will be captured by at least one scrape and at most two.
:::

Example Output:
Following metric is reported when `ExpectedScrapeIntervalSeconds` is set to `15` seconds
```
# TYPE eventstore_writer_flush_size_max gauge
eventstore_writer_flush_size_max{range="16-20 seconds"} 1854 1688070655500
```
In above example, maximum reported is `1854`. It is not a maximum measurement in last `15s` but rather maximum measurement in last `16` to last `20` seconds i.e. the maximum measurement could have been recorded in last `16s`, last `17s`, ...., upto last `20s`.
