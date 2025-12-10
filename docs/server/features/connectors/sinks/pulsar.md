---
title: 'Pulsar Sink'
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Apache Pulsar Sink connector writes events from your KurrentDB stream to a specified Pulsar topic.

## Quickstart

You can create the Pulsar Sink connector as follows. Replace `{{id}}` with your desired connector ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

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
```

After running the command, verify the connector status by checking the management API or connector logs. See [Management API Reference](../manage.md).

## Settings

The connector settings control how it interacts with Pulsar, manages message partitioning, and ensures resilience in message handling. 

::: tip
The Pulsar sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

### General settings 

| Name                   | Details                                                                                                     |
| ---------------------- | ----------------------------------------------------------------------------------------------------------- |
| `topic`                | _required_<br><br>**Description:**<br>The Pulsar topic where records are published. to.                     |
| `url`                  | **Description:**<br>The service URL for the Pulsar cluster.<br><br>**Default**: `"pulsar://localhost:6650"` |
| `defaultHeaders`       | **Description:**<br>Headers included in all produced messages.<br><br>**Default**: Empty                    |
| `authentication:token` | **Description:**<br>A JSON web token for authenticating the connector with Pulsar.                          |

### Partitioning

Partitioning options determine how the connector assigns partition keys, which affect message routing and topic compaction. 

| Name                                | Details                                                                                                                                                                                                                                                           |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `partitionKeyExtraction:enabled`    | **Description:**<br>Enables partition key extraction.<br><br>**Default**: `"false"`                                                                                                                                                                                      |
| `partitionKeyExtraction:source`     | **Description:**<br>The source for extracting the partition key.<br><br>**Accepted Values:**`"stream"`, `"streamSuffix"`, `"headers"`<br><br>**Default**: `"partitionKey"`                                                                                                   |
| `partitionKeyExtraction:expression` | **Description:**<br>A regex (for `stream` source) or a comma-separated list of header keys (for `headers` source) used to extract or combine values for the partition key. When using headers, values are concatenated with a hyphen (for example, `value1-value2`). |

### Resilience

The Pulsar sink connector relies on its own retry mechanism and doesn't include the configuration from [Resilience configuration](../settings.md#resilience-configuration).

| Name                       | Details                                                                                                                                           |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `resilience:enabled`       | **Description:**<br>Enables resilience features for message handling.<br><br>**Default**: `"true"`                                                   |
| `resilience:retryInterval` | **Description:**<br>Retry interval in seconds. Must be greater than 0.<br><br>**Format:** seconds or `"HH:MM:SS"`.<br><br> **Default:** `"00:00:03"` |

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

### Partitioning

The Pulsar sink connector allows customizing the partition keys that are sent
with the message. 

By default, it will use `"partitionKey"` and the message will be distributed
using round-robin partitioning across the available partitions in the topic. 

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

You can create partition keys by combining values from a record's metadata.

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

Specify the header keys you want to use in the `partitionKeyExtraction:expression` field (e.g., `key1,key2`). The connector will concatenate the header values with a hyphen (`-`) to create the partition key.

For example, if your event has headers `key1: regionA` and `key2: zone1`, the partition key will be `regionA-zone1`.
