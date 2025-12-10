---
title: 'Kafka Source'
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Kafka Source Connector enables you to consume messages from an existing
Kafka topic and append them into a KurrentDB stream.

## Quick Start

You can create the Kafka Source connector as follows. Replace `{{id}}` with your desired connector ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "kafka-source",
    "topic": "{{topic}}",
    "consumer:bootstrapServers": "localhost:9092",
    "schemaName": "{{schemaName}}"
  }
}
```

## Settings

### Core Options

| Name                | Details                                                                                                    |
|---------------------|------------------------------------------------------------------------------------------------------------|
| `topic`             | _required_<br><br>**Description:** Kafka topic to consume from.                                            |
| `schemaName`        | _required_<br><br>**Description:** Name of the schema used for message.                                    |
| `partition`         | **Description:** Specific partition to consume from (leave empty for all partitions).                      |
| `offset`            | **Description:** Starting offset position (leave empty for default behavior).                              |
| `preserveMagicByte` | **Description:** Whether to preserve Kafka's schema registry magic byte.<br><br>**Default**: `false`       |

### Concurrency Configuration

The Kafka Source Connector processes messages in parallel with multiple consumer tasks. Each task has its own consumer
and buffer, all sharing the group `kurrentdb-{ConnectorId}`. Their channels run independently and merge into one output
stream for efficient multi-partition handling.

| Name                          | Details                                                                                                                                                 |
|-------------------------------|:--------------------------------------------------------------------------------------------------------------------------------------------------------|
| `concurrency:channelCapacity` | **Description:** Capacity of each consumer's bounded channel. Higher values provide more buffering but consume more memory.<br><br>**Default**: `10000` |
| `concurrency:tasks`           | **Description:** The maximum number of tasks to create for this connector.<br><br>**Default**: `1`                                                      |

### Consumer Configuration

The `Consumer` section accepts standard Confluent Kafka consumer configuration options. Key options include:

| Name                            | Details                                                                                                                                                   |
|---------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `consumer:bootstrapServers`     | **Description:** Comma-separated list of Kafka broker addresses.<br><br>**Default**: `"localhost:9092"`                                                   |
| `consumer:autoOffsetReset`      | **Description:** Offset reset behavior when there is no initial offset.<br><br>**Default**: `Earliest`                                                    |
| `consumer:enableAutoCommit`     | **Description:** Enable automatic offset commits.<br><br>**Default**: `true`                                                                              |
| `consumer:autoCommitIntervalMs` | **Description:** Auto-commit interval in milliseconds.<br><br>**Default**: `5000`                                                                         |
| `consumer:sessionTimeoutMs`     | **Description:** Session timeout in milliseconds.<br><br>**Default**: `45000`                                                                             |
| `consumer:enablePartitionEof`   | **Description:** Enable end-of-partition notifications.<br><br>**Default**: `true`                                                                        |
| `consumer:securityProtocol`     | **Description:** SASL mechanism to use for authentication.<br><br>**Accepted Values:**`plaintext`, `ssl`, `saslPlaintext`<br><br>**Default**: `plaintext` |
| `consumer:saslMechanism`        | **Description:** Protocol used to communicate with brokers.<br><br>**Accepted Values:**`plain`<br><br>**Default**: `""`                                   |
| `consumer:sslCaPem`             | **Description:** CA certificate string (PEM format) for verifying the broker's key.                                                                       |
| `consumer:sslCertificatePem`    | **Description:** Client's public key string (PEM format) used for authentication.                                                                         |
| `consumer:sslKeyPem`            | **Description:** Client's private key string (PEM format) used for authentication.                                                                        |
| `consumer:saslUsername`         | **Description:** SASL username for use with the PLAIN.                                                                                                    |
| `consumer:saslPassword`         | **Description:** SASL password for use with the PLAIN.                                                                                                    |

### Stream Routing Configuration

The `stream` section configures how Kafka messages are routed to KurrentDB streams:

| Name                | Details                                                                                                                                                                                                                                                                                                                        |
|---------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `stream:strategy`   | **Description:** Stream routing strategy. <br><br>**Accepted Values:**<br>- `topic`: Route to stream named after the Kafka topic<br>- `partitionKey`: Route based on Kafka message key<br>- `fixed`: Route all messages to a single stream<br>- `header`: Extract stream name from message headers<br><br>**Default**: `topic` |
| `stream:expression` | **Description:** Expression for custom routing, depending on strategy.<br>- For `fixed`: stream name (defaults to topic name if not provided)<br>- For `header`: comma-separated header names to check<br>- Otherwise: not used                                                                                                |

### Authentication

The default authentication method is `plaintext`. You can configure the authentication method to use by setting the
`authentication:securityProtocol` option.

#### SSL/TLS

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
    "instanceTypeName": "kafka-source",
    "topic": "customers",

    "consumer:securityProtocol": "ssl",
    "consumer:sslCaPem": "...",
    "consumer:sslCertificatePem": "...",
    "consumer:sslKeyPem": "..."
}
```

#### SASL/PLAIN

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
    "instanceTypeName": "kafka-source",
    "topic": "customers",

    "consumer:securityProtocol": "saslPlaintext",
    "consumer:saslMechanism": "plain",
    "consumer:saslUsername": "user",
    "consumer:saslPassword": "pass"
}
```
