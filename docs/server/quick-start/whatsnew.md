---
order: 2
---

# What's New

## New in 25.1

These are the new features and important changes and in KurrentDB 25.1:

Features
* [Secondary Indexing](#secondary-indexing)
* [Schema Registry](#schema-registry)
* [Multi-stream Appends](#multi-stream-appends)
* [Log Record Properties](#)
* [Windows Service](#windows-service)
* [Open Telemetry Logs Export](#open-telemetry-logs-export)

Changes / Improvements
* [ServerGC](#server-garbage-collection)
* [StreamInfoCacheCapacity default](#streaminfocachecapacity-default)
* [Connectors Improvements](#)
* [Archiving Improvements](#)
* [Additional Projection Metrics](#additional-projection-metrics)
* [Additional other metrics? incorporate? ](#)
* [Misc Quality of Life Improvements]()

For breaking changes and deprecation notices, see the [upgrade guide](upgrade-guide.md).

### Secondary Indexing

```
TODO: Description and link to documentation.
```

### Schema Registry

```
TODO: Description and link to documentation.
```

### Multi-stream Appends

The server now supports appending to multiple streams atomically in one write request.

Events `e1, ..., eN` can be appended to stream `s1` and events `f1, ..., fN` being appended to stream `s2`, and so on with other streams, all in one atomic operation.

An optimistic concurrency check can be provided for each stream, and the write will only take effect if all of the checks are successful.

Client support for this feature is in progress.

### Log record properties

Historically events have been appended with optional bytes for event metadata. The server now supports receiving this data in a structured way.

```
TODO: devex to write something here
```

Client support for this feature is in progress.

### Windows Service

KurrentDB can now be run as a Windows Service. See the [documentation](installation.md#running-as-a-service) for more information.

### Open Telemetry logs export

<Badge type="info" vertical="middle" text="License Required"/>

The [Open Telemetry Integration](../diagnostics/integrations.md#opentelemetry-exporter) can now be used to export logs as well as metrics.

### Server garbage collection

The .NET runtime Server Garbage Collection is now enabled by default, increasing the performance of the server. See [the documentation](../configuration/db-config.md#garbage-collection) for more information.

### StreamInfoCacheCapacity default

`StreamInfoCacheCapacity` is now `100,000` by default rather than `0` (dynamically sized).

StreamInfoCache dynamic sizing was introduced introduced in v21.10 and enabled by default. It allows the StreamInfoCache to grow much larger, according to the amount of free memory which is reevaluated periodically. This is desirable for some workloads, but it comes with a tradeoff of very significantly increased managed memory usage, which in turn causes additional GC pressure and can lead to more frequent elections. On balance we have decided that a default of 100,000 (which is the value used before v21.10) is a better default, favouring its predictability and stability.

Users wishing to keep dynamic sizing can enable it by setting StreamInfoCacheCapacity to 0. Additional can be found in the [StreamInfoCache documentation](../configuration/README.md#streaminfocachecapacity)

### Connectors improvements

Connectors no longer periodically acquire leases, reducing the number of events they write to the database.

### Archiving Improvements

The headers of remote chunks are now only read on demand and not on startup, improving startup times when there are a large number of remote chunks.

### Additional projection metrics

Several new metrics have been added to track important properties of projections.

- `kurrentdb_projection_state_size` contains the state size of projections and their state partitions that are over 50% of the state size limit (`MaxProjectionStateSize`).
  This helps to show if any projections are in danger of reaching the limit.

- `kurrentdb_projection_state_size_bound` contains the projection state size `LIMIT` (driven by `MaxProjectionStateSize`) and the `THRESHOLD` for displaying a projection or partition in `kurrentdb_projection_state_size` (50% of the limit).
  This makes it easy to graph what the limit is and how close projections are to it.

- `kurrentdb_projection_state_serialization_duration_max_seconds` contains the recent maximum time that each custom projection has taken to serialize its state.

- `kurrentdb_projection_execution_duration_max_seconds` contains the recent maximum time that each custom projection has taken to execute an event.

- `kurrentdb_projection_execution_duration_seconds_bucket` creates a histogram for each (Projection x Function) pair showing, for example, the distribution of how long each custom projection takes to process each event type.
  This creates a lot of timeseries and is off by default. It can be enabled by setting `ProjectionExecutionByFunction` to true in `metricsconfig.json`.
  Typically this would only be enabled in development environments.

See [the documentation](../diagnostics/metrics.md#projections) for more information.

### Additional persistent subscription metrics

- `kurrentdb_persistent_sub_parked_message_replays` counts the number of messages that have been parked by stream, group, and reason. Reason can be `client-nak` meaning that the client `nak`ed the message, or `max-retries` meaning that the server retried sending it to the clients until the maximum attempts was reached.

- `kurrentdb_persistent_sub_park_message_requests` counts the number of requests to replay parked messages by stream and group.

See [the documentation](../diagnostics/metrics.md#persistent-subscriptions) for more information.

### Miscellaneous quality of life improvements

- Added server configuration option for TCP read expiry

  The option is `TcpReadTimeoutMs` and it defaults to 10000 (10s, which matches the previous behavior).

  It applies to reads received via the TCP client API. When a read has been in the server queue for longer than this, it will be discarded without being executed. If your TCP clients are configured to timeout after X milliseconds, it is advisable to set this server option to be the same, so that the server will not execute reads that the client is no longer waiting for.

  For gRPC clients, the server-side discarding is already driven by the deadline on the read itself without requiring server configuration.

- Log output to `Seq`

  You can now configure a [Seq](https://datalust.co/) log output by adding the following to `logconfig.json`:
  ```
      "Serilog": {
          "WriteTo": [
              {
                  "Name": "Seq",
                  "Args": {
                      "serverUrl": "http://localhost:5341",
                      "restrictedToMinimumLevel": "Information"
                  }
              }
          ]
      }
  ```

- Added logging for significant garbage collections

  This makes it clear from the logs if slow messages or leader elections are attributable to Garbage Collection (GC).

  Execution engine (EE) suspensions longer than 48ms are logged as Information. Execution engine suspensions longer than 600ms are logged as Warnings. Full compacting GC start/end are logged as Information.

  Note that the Start/End log messages may both be logged AFTER the execution engine pause has completed.

  These will be logged even if the node shortly goes offline for truncation, which would likely prevent the EE suspension from appearing in the metrics.

  If GC is determined as the cause of a leader election, a sensible course of action could be to reduce the Stream Info Cache Capacity (say, to the 100k traditional value) and/or consider enabling ServerGC.

  Example logs:
  ```
  [34144,13,11:03:05.307,INF] Start of full blocking garbage collection at 06/06/2025 10:02:49. GC: #210548. Generation: 2. Reason: LargeObjectHeapAllocation. Type: BlockingOutsideBackgroundGC.
  [34144,13,11:03:05.307,INF] End of full blocking garbage collection at 06/06/2025 10:03:05. GC: #210548. Took: 15,727ms
  [34144,13,11:03:05.307,WRN] Garbage collection: Very long Execution Engine Suspension. Reason: GarbageCollection. Took: 15,727ms
  ```

- Lower Scavenge API GET calls to Verbose

  The auto-scavenge checks on the status of in-progress scavenges frequently, which was producing unnecessary logs.

- Added extra logging when UnwrapEnvelopeMessage is slow

  When `UnwrapEnvelopeMessage` triggers a `SLOW QUEUE MESSAGE` log, it now includes the name of the action it was unwrapping.

## New in 25.0

These are the new features in KurrentDB 25.0:

* [Archiving](#archiving)
* [Connectors](#connectors)
* [KurrentDB rebranding](#kurrentdb-rebranding)
* [New embedded Web UI](#new-embedded-web-ui)
* [New versioning scheme and release schedule](#new-versioning-scheme-and-release-schedule)

### Archiving

<Badge type="info" vertical="middle" text="License Required"/>

KurrentDB 25.0 introduces the initial release of Archiving: a new major feature to reduce costs and increase scalability of a KurrentDB cluster.

With the new Archiving feature, data is uploaded to cheaper storage such as Amazon S3 and then can be removed from the volumes attached to the cluster nodes. The volumes can be correspondingly smaller and cheaper. The nodes are all able to read the archive, and when a read request from a client requires data that is stored in the archive, the node retrieves that data from the archive transparently to the client.

Refer to [the documentation](../features/archiving.md) for more information about archiving and instructions on how to set it up.

### Connectors

#### Elasticsearch sink

<Badge type="info" vertical="middle" text="License Required"/>

The Elasticsearch sink pulls messages from a KurrentDB stream and stores them in
an Elasticsearch index. The records will be serialized into JSON documents,
compatible with Elasticsearch's document structure.

#### Data Protection

We've introduced a comprehensive data protection system to enhance the security of your sensitive connector configurations.

All connectors now use envelope encryption to automatically protect sensitive data such as passwords and tokens using industry-standard encryption techniques. This ensures your credentials remain secure during transmission.

Setup is straightforward with token-based protection requiring minimal configuration. You can provide tokens directly in your configuration or via separate files for enhanced security in production environments.

We've integrated a native Surge key vault that stores encryption keys directly within KurrentDB system streams but we will support more key vaults in the future.

See the [Data Protection documentation](../features/connectors/features.md#data-protection) for complete setup instructions.

### KurrentDB rebranding

Event Store – the company and the product – are rebranding as Kurrent.

As part of this rebrand, EventStoreDB has been renamed to KurrentDB, with the first release of KurrentDB being version 25.0.

Read more about the rebrand in the [rebrand FAQ](https://www.kurrent.io/blog/kurrent-re-brand-faq).

The KurrentDB packages are still hosted on [Cloudsmith](https://cloudsmith.io/~eventstore/repos/kurrent/packages/). Refer to [the upgrade guide](./upgrade-guide.md) to see what's changed between EventStoreDB and KurrentDB, or [the installation guide](./installation.md) for updated installation instructions.

### New embedded Web UI

In the new embedded Web UI you can see at a glance:

- A summary of the cluster status
- A summary of resource utilization
- Recent log messages
- Node configuration
- License status

### New versioning scheme and release schedule

We are changing the version scheme with the first official release of KurrentDB.

As before, there will be two categories of release:
* Long term support (LTS) releases which are supported for a minimum of two years, with a two month grace period.
* Short term support (STS) releases which are supported until the next LTS or STS release.

The version number will now reflect whether a release is an LTS or feature release, rather than being based on the year and month. LTS releases will have _even_ major numbers, and STS releases will have _odd_ major numbers.

#### Versioning scheme

The new scheme is `Major.Minor.Patch` where:
* `Major`
    * Is _even_ for LTS releases.
    * Is _odd_ for STS releases.
* `Minor`
    * Increments with scope changes or new features.
    * Is typically `0` for LTS releases, but may be incremented in rare cases.
* `Patch` for bug fixes.

#### New release schedule

The release schedule will be changing with the versioning scheme, given that the version numbers are no longer tied to the year and month:

* LTS: Approximately one LTS release per year.
* STS: Published as necessary when sets of features are ready.
* Patch (LTS and STS): Published as necessary with bugfixes.

[More information](../release-schedule/)

#### New package repositories

Packages for KurrentDB will still be published to [Cloudsmith](https://cloudsmith.io/~eventstore), into the following repositories:

- [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts) containing only production-ready [LTS](../release-schedule/#long-term-support-releases) packages.
- [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest) containing production-ready LTS and [STS](../release-schedule/#short-term-support-releases) packages.
- [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview) containing non-production preview packages.

[More information](../quick-start/installation.md)

## New in 24.10

These are the new features that were added in EventStoreDB 24.10:

* [Connectors](#connectors):
    * Kafka
    * MongoDB
    * RabbitMQ
    * HTTP
* [Auto-scavenge](#auto-scavenge): Schedule and execute scavenges automatically across a cluster.
* [Stream policy](#stream-policy): Define stream access policies based on stream prefixes rather than using stream ACLs.
* [Encryption-at-rest](#encryption-at-rest): Encrypt EventStoreDB chunks to secure them against attackers with file access to the database.

### Connectors

We have improved and expanded on the Connectors preview introduced in 24.2.0.

The Connectors feature is enabled by default.
You can use the HTTP sink without a license, but a license is required for all other connectors.

Refer to the [documentation](../features/connectors/README.md) for instructions on setting up and configuring connectors and sinks.

#### Elasticsearch sink

<Badge type="info" vertical="middle" text="License Required"/>

The Elasticsearch sink pulls messages from a KurrentDB stream and stores them in
an Elasticsearch index. The records will be serialized into JSON documents,
compatible with Elasticsearch's document structure.

Refer to the [documentation](../features/connectors/sinks/elasticsearch.md) for instructions on setting up a Elasticsearch sink.

#### Kafka sink

<Badge type="info" vertical="middle" text="License Required"/>

The Kafka sink writes events from EventStoreDB to a Kafka topic.

It can extract the partition key from the record based on specific sources such as the stream ID, headers, or record key and also supports basic authentication and resilience
features to handle transient errors.

Refer to the [documentation](../features/connectors/sinks/kafka.md) for instructions on setting up a Kafka sink.

#### MongoDB sink

<Badge type="info" vertical="middle" text="License Required"/>

The MongoDB sink pulls messages from an EventStoreDB stream and stores the messages to a collection.

It supports data transformation for modifying event data or metadata and the inclusion of additional headers before sending messages to the MongoDB collection. It also supports at-least-once delivery and resilience features to handle transient errors.

Refer to the [documentation](../features/connectors/sinks/mongo.md) for instructions on setting up a MongoDB sink.

#### RabbitMQ sink

<Badge type="info" vertical="middle" text="License Required"/>

The RabbitMQ sink pulls messages from EventStoreDB and sends the messages to a RabbitMQ exchange using a specified routing key.

It efficiently handles message delivery by abstracting the complexities of RabbitMQ's exchange and queue management, ensuring that messages are routed to the appropriate destinations based on the provided routing key.

This sink is designed for high reliability and supports graceful error handling and recovery mechanisms to ensure consistent message delivery in a production environment.

Refer to the [documentation](../features/connectors/sinks/rabbitmq.md) for instructions on setting up a RabbitMQ sink.

#### HTTP sink

The HTTP sink allows for integration between EventStoreDB and external
APIs over HTTP or HTTPS. This connector consumes events from an EventStoreDB
stream and converts each event's data into JSON format before sending it in the
request body to a specified URL. Events are sent individually as they are
consumed from the stream, without batching. The event data is transmitted as the
request body, and metadata can be included as HTTP headers. The connector also
supports Basic Authentication and Bearer Token Authentication.

Refer to the [documentation](../features/connectors/sinks/http.md) for instructions on setting up an HTTP sink.

#### Serilog sink

The Serilog sink logs detailed messages about the connector and record details.

Refer to the [documentation](../features/connectors/sinks/serilog.md) for instructions on setting up a Serilog sink.

#### Data Protection

We've introduced a comprehensive data protection system to enhance the security of your sensitive connector configurations.

All connectors now use envelope encryption to automatically protect sensitive data such as passwords and tokens using industry-standard encryption techniques. This ensures your credentials remain secure during transmission.

Setup is straightforward with token-based protection requiring minimal configuration. You can provide tokens directly in your configuration or via separate files for enhanced security in production environments.

We've integrated a native Surge key vault that stores encryption keys directly within KurrentDB system streams but we will support more key vaults in the future.

See the [Data Protection documentation](../features/connectors/features.md#data-protection) for complete setup instructions.

### Auto-scavenge

<Badge type="info" vertical="middle" text="License Required"/>

The auto-scavenge feature automatically schedules _cluster scavenges_ which are composed of multiple _node scavenges_. Only one node scavenge can be executed at a time in the cluster. The auto-scavenge feature allows the scheduling of said _cluster scavenges_.

The auto-scavenge feature requires a license to use. EventStoreDB will only start auto-scavenging once an administrator has set up a schedule for running cluster scavenges.

Refer to the [documentation](../operations/auto-scavenge.md) for instructions on enabling and using this feature.

### Stream policy

<Badge type="info" vertical="middle" text="License Required"/>

Define stream access policies in one place based on stream prefixes rather than using stream ACLs.

Stream access policies can be created to grant users or groups read, write, delete, or metadata access.  These policies can be applied to streams based on their prefix or to system or user streams.

The Stream Policy feature requires a license to use. Refer to the [documentation](../security/user-authorization.md#stream-policy-authorization) for more information about using and configuring this feature.

### Encryption-at-rest

<Badge type="info" vertical="middle" text="License Required"/>

Encrypt EventStoreDB chunks to secure them against attackers with file access to the database.

This feature aims to protect against an attacker who obtains access to the physical disk. In contrast to volume or filesystem encryption, file-level encryption provides some protection for attacks against the live system or remote exploits, as the plaintext data is not directly readable.

The Encryption-at-rest feature requires a license to use and is disabled by default.
If Encryption-at-rest is enabled, it is impossible to roll back to an unencrypted database after a new chunk has been created or if a chunk has been scavenged.

Refer to the [documentation](../security/README.md#encryption-at-rest) for more information about using and configuring this feature.

### Enterprise features now require a license key

Customers can unlock the enterprise features of EventStoreDB with a license key. This applies to the previous commercial plugins and several of the new features in this release.

You will need to provide a license key if you want to enable or use the following features:
* Auto-scavenge
* Kafka connectors
* Stream Policies
* Encryption-at-rest
* Ldaps authentication
* OAuth authentication
* Logs Endpoint
* OTLP Endpoint
