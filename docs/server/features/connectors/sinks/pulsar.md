---
title: 'Pulsar Sink'
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Apache Pulsar sink connector enables writing events to a specified topic. It
supports extracting partition keys from records using various sources, such as
the stream ID, headers, or record key. Additionally, it provides support for
basic authentication to ensure secure communication.

## Quickstart

You can create the Pulsar Sink connector as follows:

::: tabs
@tab Powershell

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

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/pulsar-sink-connector
```

@tab Bash

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

curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/pulsar-sink-connector
```

:::

After creating and starting the Pulsar sink connector, every time an event is
appended to the `example-stream`, the Pulsar sink connector will send the record
to the specified topic. You can find a list of available management API
endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your Pulsar
sink connector with KurrentDB, ensuring it operates according to your
requirements and preferences.

::: tip
The Pulsar sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The Pulsar sink can be configured with the following options:

| Name                   | Details                                                                                                                                                                                                                    |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `topic`                | _required_<br><br>**Type**: string<br><br>**Description:** The topic to produce records to.                                                                                                                                |
| `url`                  | _protected_<br><br>**Type**: string<br><br>**Description:** The service URL for the Pulsar cluster.<br><br>**Default**: `"pulsar://localhost:6650"`                                                                        |
| `defaultHeaders`       | **Description**: Headers to include in all messages in addition to KurrentDB system headers. Specify by using `defaultHeaders:` prefix followed by the header key name.<br><br>**Example**: `"defaultHeaders:AppName": "Kurrent"` <br><br>**Default**: None |
| `authentication:token` | _protected_<br><br>**Type**: string<br><br>**Description:** The security token used for JSON web token based authentication.                                                                                               |

### Partitioning

| Name                                | Details                                                                                                                                                                           |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `partitionKeyExtraction:enabled`    | **Type**: boolean<br><br>**Description:** Enables partition key extraction.<br><br>**Default**: false                                                                             |
| `partitionKeyExtraction:source`     | **Type**: Enum<br><br>**Description:** Source for extracting the partition key.<br><br>**Accepted Values:**`stream`, `streamSuffix`, `headers`<br><br>**Default**: `PartitionKey` |
| `partitionKeyExtraction:expression` | **Type**: string<br><br>**Description:** Regular expression for extracting the partition key.                                                                                     |

See the [Partitioning](#partitioning-1) section for examples.

### Resilience

The Pulsar sink connector uses the resilience configuration and behavior provided by Apache Pulsar. The settings can be configured as follows:

| Name                       | Details                                                                                                                  |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `resilience:enabled`       | **Type**: boolean<br><br>**Description:** Enables resilience features for message handling.<br><br>**Default**: `"true"` |
| `resilience:retryInterval` | Retry interval in seconds (format: `"seconds"` or `"HH:MM:SS"`, must be greater than 0, default: `"00:00:03"`)           |

## Examples

### Partitioning

The Pulsar sink connector supports parsing and using partition keys, which are
short identifiers for messages. These keys are useful for features like topic
compaction and partitioning.

Pulsar partition keys can be generated from various sources. These sources
include the event stream, stream suffix, headers, or other record fields.

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
format, and you want to use only the trailing part as the partition key. For
example, if the stream is named `user-123`, the partition key would be `123`.

**Partition using header values**

You can generate the partition key by concatenating values from specific event
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
