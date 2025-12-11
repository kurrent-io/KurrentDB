---
title: "Kafka Sink"
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Kafka sink writes events to a Kafka topic using an idempotent producer for
reliable delivery. It can extract the partition key from the record based on
specific sources such as the stream ID, headers, or record key and also supports
basic authentication and resilience features to handle transient errors.

## Prerequisites

Before using the Kafka sink connector, ensure you have:

- A data protection token configured in your KurrentDB instance (required to encrypt sensitive fields like passwords)
- A Kafka cluster with accessible broker addresses
- Appropriate authentication credentials if using SASL

::: tip
See the [Data Protection](../features.md#data-protection) documentation for instructions on configuring the encryption token.
:::

## Quickstart

You can create the Kafka Sink connector as follows. Replace `{id}` with your desired connector ID:

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "kafka-sink",
    "bootstrapServers": "localhost:9092",
    "topic": "customers",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
```

After creating and starting the Kafka sink connector, every time an event is
appended to the `example-stream`, the Kafka sink connector will send the record
to the specified Kafka topic. You can find a list of available management API
endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your Kafka sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The Kafka sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The Kafka sink can be configured with the following options:

| Name               | Details                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------- |
| `topic`            | _required_<br><br>**Description:**<br>The Kafka topic to produce records to.                               |
| `bootstrapServers` | **Description:**<br>Comma-separated list of Kafka broker addresses.<br><br>**Default**: `"localhost:9092"` |
| `defaultHeaders`   | **Description:**<br>Headers included in all produced messages.<br><br>**Default**: Empty                   |

#### Authentication

| Name                              | Details                                                                                                                                                                         |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `authentication:securityProtocol` | **Description:**<br>Protocol used for Kafka broker communication.<br><br>**Default**: `"plaintext"`<br><br>**Accepted Values:**<br> - `"plaintext"`, `"saslPlaintext"` or `"saslSsl"` |
| `authentication:saslMechanism`    | **Description:**<br>SASL mechanism to use for authentication.<br><br>**Default**: `"plain"`<br><br>**Accepted Values:**<br> - `"plain"`, `"scramSha256"`, or `"scramSha512"`          |
| `authentication:username`         | **Description:**<br>SASL username                                                                                                                                               |
| `authentication:password`         | **Description:**<br>SASL password                                                                                                                                               |


#### Partitioning

| Name                                | Details                                                                                                                                                                                     |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `partitionKeyExtraction:enabled`    | **Description:**<br>Enables partition key extraction.<br><br>**Default**: `"false"`                                                                                                         |
| `partitionKeyExtraction:source`     | **Description:**<br>Source for extracting the partition key. See [Partitioning](#partitioning-1)<br><br>**Accepted Values:**`"partitionKey"`, `"stream"`, `"streamSuffix"`, or `"headers"`<br><br>**Default**: `"partitionKey"` |
| `partitionKeyExtraction:expression` | **Description:**<br>Regular expression for extracting the partition key.                                                                                                                    |

See the [Partitioning](#partitioning-1) section for examples.

#### Resilience

The Kafka sink connector relies on its own Kafka retry mechanism and doesn't include the configuration from [Resilience configuration](../settings.md#resilience-configuration).

| Name                               | Details                                                                                                                                                                                                    |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `waitForBrokerAck`                 | **Description:**<br>Whether the producer waits for broker acknowledgment before considering the send operation complete. See [Broker Acknowledgment](#broker-acknowledgment)<br><br>**Default**: `"false"` |
| `resilience:reconnectBackoffMaxMs` | **Description:**<br>The maximum time to wait before reconnecting to a broker after the connection has been closed.<br><br>**Default**: `"20000"`                                                           |
| `resilience:messageSendMaxRetries` | **Description:**<br>How many times to retry sending a failing Message.<br><br>**Default**: `"2147483647"`                                                                                                  |

#### Miscellaneous

| Name                  | Details                                                                                                                                                  |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `brokerAddressFamily` | **Description:**<br>Allowed broker IP address families.<br><br>**Default**: `"V4"`<br><br>**Accepted Values:** `"Any"`,`"V4"`, or `"V6"`                 |
| `compression:type`    | **Description:**<br>Kafka compression type.<br><br>**Default**: `"Zstd"`<br><br>**Accepted Values:** `"None"`, `"Gzip"`,`"Lz4"`, `"Zstd"`, or `"Snappy"` |
| `compression:level`   | **Description:**<br>Kafka compression level.<br><br>**Default**: `"6"`                                                                                   |

## Delivery Guarantees

The Kafka sink provides at-least-once delivery by using Kafka's idempotent
producer and retry settings. Messages are only checkpointed after successful
delivery confirmation from Kafka.

The `waitForBrokerAck` setting controls *when the connector waits* for the
broker acknowledgment:

- If enabled, the connector waits for the broker to confirm
  delivery before moving its checkpoint. This reduces throughput but increases
  backpressure visibility.
- If disabled, the connector does not block while waiting for the broker response. However,
  checkpointing still only occurs after the broker confirms delivery.

The Kafka sink uses an idempotent producer by default, so when writing to a
single partition, Kafka preserves message order regardless of which produce API
is used.

If a failure occurs before acknowledgment, the retry mechanism will redeliver.
After a restart, the connector resumes from the last checkpointed position,
which may cause previously sent—but uncheckpointed—messages to be reprocessed.

## Headers

The Kafka sink connector lets you include custom headers in the message headers
it sends to your topic. To add custom headers, use the `defaultHeaders` setting
in your connector configuration. Each custom header should be specified with the
prefix `defaultHeaders:` followed by the header name.

Example:

```http
PUT /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "defaultHeaders:X-API-Key": "your-api-key-here",
  "defaultHeaders:X-Tenant-ID": "production-tenant",
  "defaultHeaders:X-Source-System": "KurrentDB"
}
```

These headers will be included in every message sent by the connector, in addition to the [default headers](../features.md#headers) automatically added by the connector's plugin.

## Examples

### Authentication

The Kafka sink connector supports secure communication with Kafka brokers using
**SASL authentication**. By default, the connector communicates in **plaintext**
without authentication. However, you can configure it to use SASL with different
security protocols and authentication mechanisms.

::: note
When using `saslSsl`, the connector uses your system's trusted CA certificates
for SSL/TLS encryption. This works with managed services like **AWS MSK**,
**Confluent Cloud**, and **Azure Event Hubs**. For self-signed or private CA
certificates, add them to your system's trust store first.
:::

#### SASL/PLAINTEXT with PLAIN Authentication
```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslPlaintext",
  "authentication:saslMechanism": "plain",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

#### SASL/PLAINTEXT with SCRAM-SHA-256 Authentication
```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslPlaintext",
  "authentication:saslMechanism": "scramSha256",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

#### SASL/PLAINTEXT with SCRAM-SHA-512 Authentication
```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslPlaintext",
  "authentication:saslMechanism": "scramSha512",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

#### SASL/SSL with PLAIN Authentication

For production environments with encryption (recommended for managed Kafka services):
```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslSsl",
  "authentication:saslMechanism": "plain",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

#### SASL/SSL with SCRAM-SHA-256 Authentication
```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslSsl",
  "authentication:saslMechanism": "scramSha256",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

#### SASL/SSL with SCRAM-SHA-512 Authentication
```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslSsl",
  "authentication:saslMechanism": "scramSha512",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

#### Additional resources
- [Azure Event Hub Security and Authentication](https://learn.microsoft.com/en-us/azure/event-hubs/azure-event-hubs-apache-kafka-overview#security-and-authentication)
- [Set up SASL/SCRAM authentication for an Amazon MSK cluster](https://docs.aws.amazon.com/msk/latest/developerguide/msk-password-tutorial.html)
- [Use SASL/SCRAM authentication in Confluent Platform](https://docs.confluent.io/platform/current/security/authentication/sasl/scram/overview.html#use-sasl-scram-authentication-in-cp)

### Partitioning

The Kafka sink connector allows customizing the partition keys that are sent
with the message. 

By default, it will use `"partitionKey"` and the message will be distributed
using round-robin partitioning across the available partitions in the topic. 

**Partition using Stream ID**

You can extract part of the stream name using a regular expression (regex) to
define the partition key. The expression is optional and can be customized based
on your naming convention. In this example, the expression captures the stream
name up to `_data`.

```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "stream",
  "partitionKeyExtraction:expression": "^(.*)_data$"
}
```

Alternatively, if you only need the last segment of the stream name (after a
hyphen), you can use the `streamSuffix` source. This
doesn't require an expression since it automatically extracts the suffix.

```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "streamSuffix"
}
```

The `streamSuffix` source is useful when stream names follow a structured
format, and you want to use only the trailing part as the partition key. For
example, if the stream is named `user-123`, the partition key would be `123`.

**Partition using header values**

You can create partition keys by combining values from a record's metadata.

```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "headers",
  "partitionKeyExtraction:expression": "key1,key2"
}
```

Specify the header keys you want to use in the `partitionKeyExtraction:expression` field (e.g., `key1,key2`). The connector will concatenate the header values with a hyphen (`-`) to create the partition key.

For example, if your event has headers `key1: regionA` and `key2: zone1`, the partition key will be `regionA-zone1`.

## Tutorial

[Learn how to set up and use a Kafka Sink connector in KurrentDB through a tutorial.](/tutorials/Kafka_Sink.md)
