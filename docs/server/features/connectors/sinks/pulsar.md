---
title: 'Pulsar Sink'
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Apache Pulsar Sink connector writes events from your service to a specified Pulsar topic. It supports:
- **Partitioning:** Extract partition keys from various sources (e.g., stream ID, headers, record key).
- **Security:** Offers token-based authentication for secure communication.
- **Resilience:** Leverages Apache Pulsar’s built-in resilience for robust message handling.

## Pre-requisites

Before setting up the Pulsar Sink connector, ensure that:
- **Apache Pulsar Cluster:** Your Pulsar cluster is up and running.
- **Network Access:** The service URL is accessible (adjust firewall settings as needed).
- **Security Tokens:** If using authentication, have your JSON web token ready.
- **Basic Knowledge:** Familiarity with JSON and command line operations.

## Quickstart

Follow these steps to create and start the Pulsar Sink connector.

::: tabs
@tab Powershell

1. Create the JSON Configuration:

```powershell
$JSON = @"
{
  "settings": {
    "instanceTypeName": "pulsar-sink",
    "topic": "customers",
    "url": "pulsar://localhost:6650",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
"@ `
```

2. Send a POST request to create the sink connector:

```powershell
curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/pulsar-sink-connector
```

@tab Bash

1. Create the JSON Configuration:

```bash
JSON='{
  "settings": {
    "instanceTypeName": "pulsar-sink",
    "topic": "customers",
    "url": "pulsar://localhost:6650",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}'
```

2. Send a POST request to create the sink connector:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/pulsar-sink-connector
```

:::

::: tip
Replace the URL with your KurrentDB URL. The default value is `http:localhost:2113`.
:::

After running the command, verify the connector status by checking the management API or connector logs. See [Management API Reference](../manage.md).

## Settings

The connector settings control how it interacts with Pulsar, manages message partitioning, and ensures resilience in message handling. 

::: tip
The Pulsar sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

### General settings 

| Name                   | Details                                                                                                                                                                                                                    |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `topic`                | _Required_<br><br>**Description:** The Pulsar topic where records are published. to.                                                                                                                                |
| `url`                  | <br><br>**Description:** The service URL for the Pulsar cluster.<br><br>**Default**: `"pulsar://localhost:6650"`                                                                        |
| `defaultHeaders`       | **Description**: Default headers to include in all outgoing messages along with KurrentDB system headers. Prefix header names with `defaultHeaders:` followed by the header key name.<br><br>**Example**: `"defaultHeaders:AppName": "Kurrent"` <br><br>**Default**: None |
| `authentication:token` | **Description:** A JSON web token for authenticating the connector with Pulsar. |

### Partitioning

Partitioning options determine how the connector assigns partition keys, which affect message routing and topic compaction. 

| Name                                | Details                                                                                                                                                                           |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `partitionKeyExtraction:enabled`    | **Description:** Enables partition key extraction.<br><br>**Default**: false                                                                             |
| `partitionKeyExtraction:source`     | **Description:** The source for extracting the partition key.<br><br>**Accepted Values:**`stream`, `streamSuffix`, `headers`<br><br>**Default**: `PartitionKey` |
| `partitionKeyExtraction:expression` | **Description:** A regex (for `stream` source) or a comma-separated list of header keys (for `headers` source) used to extract or combine values for the partition key. When using headers, values are concatenated with a hyphen (for example, `value1-value2`).                                                                                     |

### Resilience

These settings customize the connector’s behavior in handling message failures and retries provided by Apache Pulsar.

| Name                       | Details                                                                                                                  |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `resilience:enabled`       | **Description:** Enables resilience features for message handling.<br><br>**Default**: `"true"` |
| `resilience:retryInterval` | **Description:** Retry interval in seconds. Must be greater than 0.<br><br>**Format:** seconds or `"HH:MM:SS"`.<br><br> **Default:** `"00:00:03"`           |

## Examples

These examples demonstrate how to configure partitioning, security, and other practical scenarios.

### Partition using Stream ID

Extract part of a stream name using a regex. In this example, the regex captures everything up to `_data`.

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
format, and you want to use only the trailing part as the partition key. For
example, if the stream is named `user-123`, the partition key would be `123`.

### Partition using header values

Combine multiple header values to form the partition key. This example concatenates header values `key1` and `key2` using a hyphen.

```json
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

```json
{
  "key1": "value1",
  "key2": "value2"
}

// outputs "value1-value2"
```

:::
