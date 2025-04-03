---
title: "Kafka Sink"
order: 1
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Kafka sink writes events to a Kafka topic. It can extract the
partition key from the record based on specific sources such as the stream ID,
headers, or record key and also supports basic authentication and resilience
features to handle transient errors.

## Quickstart

You can create the Kafka Sink connector as follows:

::: tabs
@tab Powershell

```powershell
$JSON = @"
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
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/kafka-sink-connector
```

@tab Bash

```bash
JSON='{
  "settings": {
    "instanceTypeName": "kafka-sink",
    "topic": "your-topic",
    "bootstrapServers": "your-kafka-cluster-address:9092",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream",
    "authentication:username": "your-username",
    "authentication:password": "your-password",
    "authentication:securityProtocol": "SaslSsl",
    "waitForBrokerAck": "true"
  }
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/mongo-sink-connector
```

:::

After creating and starting the Kafka sink connector, every time an event is
appended to the `example-stream`, the Kafka sink connector will send the record
to the specified Kafka topic.You can find a list of available management API
endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your Kafka sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The Kafka sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The kafka sink can be configured with the following options:

| Name                              | Details                                                                                                                                                                                                                                                       |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `topic`                           | _required_<br><br>**Type**: string<br><br>**Description:** The Kafka topic to produce records to.                                                                                                                                                             |
| `bootstrapServers`                | **Type**: string<br><br>**Description:** Comma-separated list of Kafka broker addresses.<br><br>**Default**: `localhost:9092`                                                                                                                                 |
| `defaultHeaders`                  | **Type**: dict<string,string><br><br>**Description:** Headers included in all produced messages.<br><br>**Default**: None                                                                                                                                     |
| `authentication:securityProtocol` | **Type**: [SecurityProtocol](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.SecurityProtocol.html)<br><br>**Description:** Protocol used for Kafka broker communication.<br><br>**Default**: `Plaintext` |
| `authentication:username`         | **Type**: string<br><br>**Description:** Username for authentication.                                                                                                                                                                                         |
| `authentication:password`         | _protected_<br><br>**Type**: string<br><br>**Description:** Password for authentication.                                                                                                                                                                                         |

### Partitioning

| Name                                | Details                                                                                                                                                                           |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `partitionKeyExtraction:enabled`    | **Type**: boolean<br><br>**Description:** Enables partition key extraction.<br><br>**Default**: false                                                                             |
| `partitionKeyExtraction:source`     | **Type**: Enum<br><br>**Description:** Source for extracting the partition key.<br><br>**Accepted Values:**`stream`, `streamSuffix`, `headers`<br><br>**Default**: `PartitionKey` |
| `partitionKeyExtraction:expression` | **Type**: string<br><br>**Description:** Regular expression for extracting the partition key.                                                                                     |

See the [Partitioning](#partitioning-1) section for examples.

### Resilience

Besides the common sink settings that can be found in the [Resilience Configuration](../settings.md#resilience-configuration) page, the Kafka sink connector supports additional settings related to resilience:

| Name                               | Details                                                                                                                                                                                                        |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `waitForBrokerAck`                 | **Type**: boolean<br><br>**Description:** Whether the producer waits for broker acknowledgment before considering the send operation complete.<br><br>**Default**: true                                        |
| `resilience:enabled`               | **Type**: boolean<br><br>**Description:** Enables resilience features for message handling.<br><br>**Default**: `true`                                                                                         |
| `resilience:maxRetries`            | **Type**: int<br><br>**Description:** Maximum number of retry attempts.<br><br>**Default**: `-1` (unlimited)                                                                                                   |
| `resilience:transientErrorDelay`   | **Type**: TimeSpan<br><br>**Description:** Delay between retries for transient errors.<br><br>**Default**: `00:00:00`                                                                                          |
| `resilience:reconnectBackoffMaxMs` | **Type**: int<br><br>**Description:** Maximum backoff time in milliseconds for reconnection attempts.<br><br>**Default**: `20000`                                                                              |
| `resilience:messageSendMaxRetries` | **Type**: int<br><br>**Description:** Number of times to retry sending a failing message. **Note:** Retrying may cause reordering unless `enable.idempotence` is set to true.<br><br>**Default**: `2147483647` |

### Miscellaneous

| Name                  | Details                                                                                                                                                                                                                                            |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `brokerAddressFamily` | **Type**: [BrokerAddressFamily](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.BrokerAddressFamily.html)<br><br>**Description:** Allowed broker IP address families.<br><br>**Default**: `V4` |
| `compression:type`    | **Type**: [CompressionType](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.CompressionType.html)<br><br>**Description:** Kafka compression type.<br><br>**Default**: `Zstd`                   |
| `compression:level`   | **Type**: int<br><br>**Description:** Kafka compression level.<br><br>**Default**: 6                                                                                                                                                               |

## At least once delivery

The Kafka sink guarantees at least once delivery by retrying failed
requests based on configurable resilience settings. It will continue to attempt
delivery until the event is successfully sent or the maximum number of retries
is reached, ensuring each event is delivered at least once.

The Kafka sink currently retries transient errors based on the following error codes:

- **Local_AllBrokersDown**: All broker connections are down
- **OutOfOrderSequenceNumber**: Broker received an out of order sequence number
- **TransactionCoordinatorFenced**: Indicates that the transaction coordinator sending a WriteTxnMarker is no longer the
  current coordinator for a given producer
- **UnknownProducerId**: Unknown Producer Id.

For detailed information on the listed error codes, refer to
the [Kafka documentation](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.ErrorCode.html).

**Configuration example**

```json
{
  "resilience:enabled": true,
  "resilience:requestTimeoutMs": 3000,
  "resilience:maxRetries": -1,
  "resilience:transientErrorDelay": "00:00:05",
  "resilience:reconnectBackoffMaxMs": 20000,
  "resilience:messageSendMaxRetries": 2147483647
}
```

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

To learn more about authentication in Kafka,
see [Authentication using SASL](https://kafka.apache.org/documentation/#security_sasl)

For Kafka client enum types, please refer to the
official [Kafka .NET client documentation](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.html).

## Examples

### Partitioning

The Kafka sink connector writes events to Kafka topics, and it allows the
customization of partition keys. Kafka's partitioning strategy is essential for
ensuring that related messages are sent to the same partition, which helps
maintain message ordering and effective load distribution. Read more about
[Kafka Partitions](https://docs.confluent.io/kafka/introduction.html#partitions).

Kafka partition keys can be generated from various sources, similar to how
document IDs are generated in the MongoDB connector. These sources include the
event stream, stream suffix, headers, or other record fields.

By default, it will use the `PartitionKey` and grab this value from the KurrentDB record.

**Partition using Stream ID**

You can extract part of the stream name using a regular expression (regex) to
define the partition key. The expression is optional and can be customized based
on your naming convention. In this example, the expression captures the stream
name up to `_data`.

```json
{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "stream",
  "partitionKeyExtraction:expression": "^(.*)_data$"
}
```

Alternatively, if you only need the last segment of the stream name (after a
hyphen), you can use the `streamSuffix` source. This
doesn't require an expression since it automatically extracts the suffix.

```json
{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "streamSuffix"
}
```

The `streamSuffix` source is useful when stream names follow a structured
format, and you want to use only the trailing part as the document ID. For
example, if the stream is named `user-123`, the partition key would be `123`.

**Partition using header values**

You can generate the document ID by concatenating values from specific event
headers. In this case, two header values (`key1` and `key2`) are combined to
form the ID.

```json
{
  "partitionKeyExtraction:enabled": "true",
  "partitionKeyExtraction:source": "headers",
  "partitionKeyExtraction:expression": "key1,key2"
}
```

The `Headers` source allows you to pull values from the event's metadata. The
`documentId:expression` field lists the header keys (in this case, `key1` and
`key2`), and their values are concatenated to generate the document ID. This is
useful when headers hold important metadata that should define the document's
unique identifier, such as region, user ID, or other identifiers.

::: details Click here to see an example

```json
{
  "key1": "value1",
  "key2": "value2"
}

// outputs "value1-value2"
```

:::

## Tutorial

[Learn how to set up and use a Kafka Sink connector in KurrentDB through a tutorial.](/tutorials/Kafka_Sink.md)
