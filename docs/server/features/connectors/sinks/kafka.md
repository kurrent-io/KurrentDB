---
title: "Kafka Sink"
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Kafka sink writes events to a Kafka topic. It can extract the
partition key from the record based on specific sources such as the stream ID,
headers, or record key and also supports basic authentication and resilience
features to handle transient errors.

## Quickstart

You can create the Kafka Sink connector as follows. Replace `id` with a unique connector name or ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "kafka-sink",
    "topic": "customers",
    "bootstrapServers": "localhost:9092",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream",
    "authentication:username": "your-username",
    "authentication:password": "your-password",
    "authentication:securityProtocol": "SaslSsl",
    "waitForBrokerAck": "true"
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
| `defaultHeaders`   | **Description:**<br>Headers included in all produced messages.<br><br>**Default**: `"None"`                |

### Authentication

| Name                              | Details                                                                                                                                                                |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `authentication:securityProtocol` | **Description:**<br>Protocol used for Kafka broker communication.<br><br>**Default**: `"plaintext"`<br><br>**Accepted Values:** `"plaintext"` or `"saslPlaintext"`     |
| `authentication:saslMechanism`    | **Description:**<br>SASL mechanism to use for authentication.<br><br>**Default**: `"plain"`<br><br>**Accepted Values:** `"plain"`, `"scramSha256"`, or `"scramSha512"` |
| `authentication:username`         | **Description:**<br>SASL username                                                                                                                                      |
| `authentication:password`         | **Description:**<br>SASL password                                                                                                                                      |


### Partitioning

| Name                                | Details                                                                                                                                                                   |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `partitionKeyExtraction:enabled`    | **Description:**<br>Enables partition key extraction.<br><br>**Default**: `"false"`                                                                                       |
| `partitionKeyExtraction:source`     | **Description:**<br>Source for extracting the partition key.<br><br>**Accepted Values:**`"stream"`, `"streamSuffix"`, or `"headers"`<br><br>**Default**: `"partitionKey"` |
| `partitionKeyExtraction:expression` | **Description:**<br>Regular expression for extracting the partition key.                                                                                                  |

See the [Partitioning](#partitioning-1) section for examples.

### Resilience

Besides the common sink settings that can be found in the [Resilience Configuration](../settings.md#resilience-configuration) page, the Kafka sink connector supports additional settings related to resilience:

| Name                               | Details                                                                                                                                                                                            |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `waitForBrokerAck`                 | **Description:**<br>Whether the producer waits for broker acknowledgment before considering the send operation complete.<br><br>**Default**: `"true"`                                              |
| `resilience:enabled`               | **Description:**<br>Enables resilience features for message handling.<br><br>**Default**: `"true"`                                                                                                 |
| `resilience:maxRetries`            | **Description:**<br>Maximum number of retry attempts.<br><br>**Default**: `"-1"` (unlimited)                                                                                                       |
| `resilience:transientErrorDelay`   | **Description:**<br>Delay between retries for transient errors.<br><br>**Default**: `"00:00:00"`                                                                                                   |
| `resilience:reconnectBackoffMaxMs` | **Description:**<br>Maximum backoff time in milliseconds for reconnection attempts.<br><br>**Default**: `"20000"`                                                                                  |
| `resilience:messageSendMaxRetries` | **Description:**<br>Number of times to retry sending a failing message. **Note:** Retrying may cause reordering unless `enable.idempotence` is set to `"true"`.<br><br>**Default**: `"2147483647"` |

### Miscellaneous

| Name                  | Details                                                                                                                                                  |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `brokerAddressFamily` | **Description:**<br>Allowed broker IP address families.<br><br>**Default**: `"V4"`<br><br>**Accepted Values:** `"Any"`,`"V4"`, or `"V6"`                 |
| `compression:type`    | **Description:**<br>Kafka compression type.<br><br>**Default**: `"Zstd"`<br><br>**Accepted Values:** `"None"`, `"Gzip"`,`"Lz4"`, `"Zstd"`, or `"Snappy"` |
| `compression:level`   | **Description:**<br>Kafka compression level.<br><br>**Default**: `"6"`                                                                                   |

## At least once delivery

The Kafka sink guarantees at least once delivery by retrying failed
requests based on configurable resilience settings. It will continue to attempt
delivery until the event is successfully sent or the maximum number of retries
is reached, ensuring each event is delivered at least once.

**Configuration example**

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "resilience:enabled": "true",
  "resilience:requestTimeoutMs": "3000",
  "resilience:maxRetries": "-1",
  "resilience:transientErrorDelay": "00:00:05",
  "resilience:reconnectBackoffMaxMs": "20000",
  "resilience:messageSendMaxRetries": "2147483647"
}
```

The Kafka sink retries transient errors only for the following Kafka error codes:

- Local_AllBrokersDown
- OutOfOrderSequenceNumber
- TransactionCoordinatorFenced
- UnknownProducerId

For detailed descriptions of these error codes, see the official [Kafka Documentation](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.ErrorCode.html).

## Broker Acknowledgment

In the Kafka sink connector for KurrentDB, broker acknowledgment refers to
the producer waiting for confirmation from the Kafka broker that a message has
been successfully received. When `waitForBrokerAck` is enabled (which is the
default setting), the producer waits for this acknowledgment, ensuring more
reliable delivery of messages, which is crucial for systems that require
durability and fault tolerance.

While this setting improves reliability, it can slightly increase latency, as
the producer must wait for confirmation from Kafka before continuing. If higher
throughput is preferred over strict delivery guarantees, you can disable this
option.

For more details about Kafka broker acknowledgment, refer to [Kafka's official
documentation](https://kafka.apache.org/documentation/#producerconfigs_acks).

## Headers

The Kafka sink connector lets you include custom headers in the message headers
it sends to your topic. To add custom headers, use the `defaultHeaders` setting
in your connector configuration. Each custom header should be specified with the
prefix `defaultHeaders:` followed by the header name.

Example:

```http
PUT /connectors/{{id}}
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

### SASL/PLAINTEXT Authentication

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslPlaintext",
  "authentication:saslMechanism": "plain",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

### SASL/SCRAM-SHA-256 Authentication

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslPlaintext",
  "authentication:saslMechanism": "scramSha256",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

### SASL/SCRAM-SHA-512 Authentication

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:securityProtocol": "saslPlaintext",
  "authentication:saslMechanism": "scramSha512",
  "authentication:username": "my-username",
  "authentication:password": "my-password"
}
```

### Partitioning

The Kafka sink connector allows customizing the partition keys that are sent
with the message. 

Kafka partition keys can be generated from various sources. These sources
include the event stream, stream suffix, headers, or other record fields.

By default, it will use the `PartitionKey` and grab this value from the KurrentDB record.

**Partition using Stream ID**

You can extract part of the stream name using a regular expression (regex) to
define the partition key. The expression is optional and can be customized based
on your naming convention. In this example, the expression captures the stream
name up to `_data`.

```http
PUT /connectors/{{id}}/settings
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
PUT /connectors/{{id}}/settings
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

You can generate the partition key by concatenating values from specific event
headers. In this case, two header values (`key1` and `key2`) are combined to
form the key.

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "headers",
  "partitionKeyExtraction:expression": "key1,key2"
}
```

The `Headers` source allows you to pull values from the event's metadata. The
`documentId:expression` field lists the header keys (in this case, `key1` and
`key2`), and their values are concatenated to generate the partition key. 

::: details Click here to see an example

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "key1": "value1",
  "key2": "value2"
}

// outputs "value1-value2"
```

:::

## Tutorial

[Learn how to set up and use a Kafka Sink connector in KurrentDB through a tutorial.](/tutorials/Kafka_Sink.md)
