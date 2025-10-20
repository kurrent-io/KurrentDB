---
title: 'Configure Connector'
order: 3
---

All connectors share a common set of configuration options that can be used to
customize their behavior.

## Sink Options

Below, you will find a comprehensive list of configuration settings that you can
use to define the connection parameters and other necessary details for your
sink connector.

::: tip
Individual connectors also include their own specific settings. To view them, go to their individual pages.
:::

### Instance configuration

| Name               | Details                                                                                                                                                                                                                                                                                                                                                                       |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `instanceTypeName` | _required_<br><br>**Description:**<br>Specifies the type of sink connector instance.<br><br>**Accepted Values:**<br><ul><li>`serilog-sink`</li><li>`http-sink`</li><li>`kafka-sink`</li><li>`elasticsearch-sink`</li><li>`rabbit-mq-sink`</li><li>`mongo-db-sink`</li></ul>For details about each type, see the individual sink's [page](/server/features/connectors/sinks/). |

### Subscription configuration

| Name                             | Details                                                                                                                                                                                                                                                                                            |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `subscription:filter:scope`      | **Description:**<br>Determines the scope of event filtering for the subscription. Use `"stream"` to filter events by stream ID, or `"record"` to filter events by event type.<br><br>**Accepted Values:**<br>- `"stream"`, `"record"`, `"unspecified"`.<br><br>**Default**: `"unspecified"`        |
| `subscription:filter:filterType` | **Description:**<br>Specifies the method used to filter events.<br><br>**Accepted Values:**<br>- `"streamId"`, `"regex"`, `"prefix"`, `"jsonPath"`, `"unspecified"`.<br><br>**Default**: `"unspecified"`                                                                                           |
| `subscription:filter:expression` | **Description:**<br>A filter expression (regex, JsonPath, or prefix) for records. If `scope` is specified and the expression is empty, it consumes from `$all` including system events. If `scope` is unspecified, it consumes from `$all` excluding system events.<br><br>**Default**: `""` |
| `subscription:initialPosition`   | **Description:**<br>The position in the message stream from which a consumer starts consuming messages when there is no prior checkpoint.<br><br>**Accepted Values:**<br>- `"latest"`, `"earliest"`.<br><br>**Default**: `"latest"`                                                                |

For details and examples on subscriptions, see [Filters](./features.md#filters).

### Transformation configuration

| Name                   | Details                                                                                                                                                                                                                         |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `transformer:enabled`  | **Description:**<br>Enables or disables the event transformer.<br><br>**Default**: `"false"`                                                                                                                   |
| `transformer:function` | _required_<br><br>**Description:**<br>Base64 encoded JavaScript function for transforming events. See [Transformations](./features.md#transformations) for examples.<br><br>**Default**: `""` |

For details and examples on transformations, see [Transformations](./features.md#transformations).

### Resilience configuration

| Name                                       | Details                                                                                                                                        |
| ------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `resilience:enabled`                       | **Description:**<br>Enables or disables resilience.<br><br>**Default**: `"true"`                                                               |
| `resilience:firstDelayBound:upperLimitMs`  | **Description:**<br>The upper limit for the first delay bound in milliseconds.<br><br>**Default**: `"60000"`                                   |
| `resilience:firstDelayBound:delayMs`       | **Description:**<br>The delay for the first delay bound in milliseconds.<br><br>**Default**: `"5000"`                                          |
| `resilience:secondDelayBound:upperLimitMs` | **Description:**<br>The upper limit for the second delay bound in milliseconds.<br><br>**Default**: `"3600000"`                                |
| `resilience:secondDelayBound:delayMs`      | **Description:**<br>The delay for the second delay bound in milliseconds.<br><br>**Default**: `"600000"`                                       |
| `resilience:thirdDelayBound:upperLimitMs`  | **Description:**<br>The upper limit for the third delay bound in milliseconds. A value of `"-1"` indicates forever.<br><br>**Default**: `"-1"` |
| `resilience:thirdDelayBound:delayMs`       | **Description:**<br>The delay for the third delay bound in milliseconds.<br><br>**Default**: `"3600000"`                                       |

::: note
Not all connectors use every resilience setting listed here. Some may have additional or different resilience options. If a connector’s individual documentation page does not explicitly mention support for these settings, it does not use them.
:::

For more details on resilience, see [Resilience](./features.md#resilience).

### Auto-Commit configuration

| Name                          | Details                                                                                                    |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `autocommit:enabled`          | **Description:**<br>Enable or disables auto-commit<br><br>**Default:** `"true"`                            |
| `autocommit:interval`         | **Description:**<br>The interval, in milliseconds at which auto-commit occurs<br><br>**Default**: `"5000"` |
| `autocommit:recordsThreshold` | **Description:**<br>The threshold of records that triggers an auto-commit<br><br>**Default**: `"1000"`     |

### Logging configuration

| Name              | Details                                                                       |
| ----------------- | ----------------------------------------------------------------------------- |
| `logging:enabled` | **Description:**<br>Enables or disables logging.<br><br>**Default**: `"true"` |

## Disable the Plugin

The Connector plugin is pre-installed in all EventStoreDB binaries and is enabled by default. It can be disabled with the following configuration.

Refer to the [configuration guide](../../configuration/README.md) for configuration mechanisms other than YAML.

```yaml
Connectors:
  Enabled: false
```
