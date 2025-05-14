---
order: 2
---

# What's New

## New features

* Connectors:
    * Kafka
    * MongoDB
    * RabbitMQ
    * HTTP
* Auto-scavenge: Schedule and execute scavenges automatically across a cluster.
* Stream Policy: Define stream access policies based on stream prefixes rather than using stream ACLs.
* Encryption-at-rest: Encrypt EventStoreDB chunks to secure them against attackers with file access to the database.

### Connectors

We have improved and expanded on the Connectors preview introduced in 24.2.0.

The Connectors feature is enabled by default.
You can use the HTTP sink without a license, but a license is required for all other connectors.

Refer to the [documentation](../features/connectors/README.md) for instructions on setting up and configuring connectors and sinks.

#### Data Protection

We've introduced a comprehensive data protection system to enhance the security of your sensitive connector configurations.

All connectors now use envelope encryption to automatically protect sensitive data such as passwords and tokens using industry-standard encryption techniques. This ensures your credentials remain secure during transmission.

Setup is straightforward with token-based protection requiring minimal configuration. You can provide tokens directly in your configuration or via separate files for enhanced security in production environments.

We've integrated a native Surge key vault that stores encryption keys directly within EventStoreDB system streams but we will support more key vaults in the future.

See the [Data Protection documentation](../features/connectors/features.md#data-protection) for complete setup instructions.

::: note
Data protection is available as from 24.10.5 and later.
:::

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

### Auto-scavenge

<Badge type="info" vertical="middle" text="License Required"/>

The auto-scavenge feature automatically schedules _cluster scavenges_ which are composed of multiple _node scavenges_. Only one node scavenge can be executed at a time in the cluster. The auto-scavenge feature allows the scheduling of said _cluster scavenges_.

The auto-scavenge feature requires a license to use. EventStoreDB will only start auto-scavenging once an administrator has set up a schedule for running cluster scavenges.

Refer to the [documentation](../operations/auto-scavenge.md) for instructions on enabling and using this feature.

### Stream Policy

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

## Event Store License v2

Starting with the 24.10 LTS release, we are transitioning to a new licensing model: the Event Store License v2 (ESLv2).

The new license model changes how EventStoreDB is packaged and how certain features are enabled. A summary of how this affects installing and running EventStoreDB is listed below. Check out [the announcement](https://www.eventstore.com/blog/introducing-event-store-license-v2-eslv2) for the full details.

### Unified binary release

There is no longer a distinction between OSS and commercial binaries for EventStoreDB.

Instead, there is a single binary artifact that anyone can access and use, but a license key is required to use enterprise features.

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

Refer to the [documentation](../quick-start/installation.md#license-keys) for more information about using a license key.

### EventStoreDB binaries have moved to Cloudsmith

All EventStoreDB binaries are now available on [Cloudsmith](https://cloudsmith.io/~eventstore/repos/eventstore/). In addition, the RedHat binaries which were previously commercial-only can now be accessed by all users of EventStoreDB.
