---
title: "RabbitMQ Sink"
order: 3
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

This sink is responsible for sending messages to a RabbitMQ exchange using a specified routing key. It efficiently
handles message delivery by abstracting the complexities of RabbitMQ's exchange and queue management, ensuring that
messages are routed to the appropriate destinations based on the provided routing key. This sink is designed for high
reliability and supports graceful error handling and recovery mechanisms to ensure consistent message delivery in a
production environment.

## Quickstart

You can create the RabbitMQ Sink connector as follows:

::: tabs
@tab Powershell

```powershell
$JSON = @"
{
  "settings": {
    "instanceTypeName": "rabbit-mq-sink",
    "exchange:name": "example-exchange",
    "exchange:type": "direct",
    "routingKey": "my-routing-key",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/rabbit-sink-connector
```

@tab Bash

```bash
JSON='{
  "settings": {
    "instanceTypeName": "rabbit-mq-sink",
    "exchange:name": "example-exchange",
    "exchange:type": "direct",
    "routingKey": "my-routing-key",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/rabbit-sink-connector
```

:::

After creating and starting the RabbitMQ sink connector, every time an event is
appended to the `example-stream`, the RabbitMQ sink connector will send the
record to the specified queue in RabbitMQ. You can find a list of available
management API endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your RabbitMQ sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The RabbitMQ sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The RabbitMQ sink can be configured with the following options:

| Option                            | Description                                                                                                                                                                                                                                                                                         |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `exchange:name`                   | _required_<br><br>**Type**: string<br><br>**Description:** Exchange's name.                                                                                                                                                                                                                         |
| `exchange:type`                   | _required_<br><br>**Type**: string<br><br>**Description:** Exchange's type. Default is fanout.                                                                                                                                                                                                      |
| `routingKey`                      | **Type**: string<br><br>**Description:** Used by the exchange to determine how to route a message to the appropriate queue(s). The routing key is evaluated against the binding rules of the queues associated with the exchange, influencing the message's delivery path.<br><br>**Default**: `""` |
| `host`                            | **Type**: string<br><br>**Description:** Hostname of the server.<br><br>**Default**: `"localhost"`                                                                                                                                                                                                  |
| `port`                            | **Type**: string<br><br>**Description:** Port number of the server.<br><br>**Default**: `5672`                                                                                                                                                                                                      |
| `connectionName`                  | **Type**: string<br><br>**Description:** Connection name used for logging and diagnostics. The default value is the connector ID.Connection name used for logging and diagnostics.<br><br>**Default**: The connector id                                                                             |
| `virtualHost`                     | **Type**: string<br><br>**Description:** Represents the VirtualHost (vhost) used in RabbitMQ. <br><br>**Default**: `/`                                                                                                                                                                              |
| `waitForBrokerAck`                | **Type**: boolean<br><br>**Description:** Whether the channel waits for broker acknowledgment before considering the send operation complete.<br><br>**Default**: `false`                                                                                                                           |
| `authentication:username`         | **Type**: string<br><br>**Description:** Username for authentication.<br><br>**Default**: "guest"                                                                                                                                                                                                   |
| `authentication:password`         | **Type**: string<br><br>**Description:** Password for authentication.<br><br>**Default**: "guest"                                                                                                                                                                                                   |
| `autoDelete`                      | **Type**: string<br><br>**Description:** Whether the exchange is automatically deleted when no longer in use.<br><br>**Default**: `false`                                                                                                                                                           |
| `durable`                         | **Type**: string<br><br>**Description:** Whether the exchange is durable. Default true.                                                                                                                                                                                                             |
| `resilience:connectionTimeoutMs`  | **Type**: int<br><br>**Description:** Connection TCP establishment timeout in milliseconds. 0 for infinite.<br><br>**Default**: `60000`                                                                                                                                                             |
| `resilience:handshakeTimeoutMs`   | **Type**: int<br><br>**Description:** The protocol handshake timeout in milliseconds.<br><br>**Default**: `10000`                                                                                                                                                                                   |
| `resilience:requestedHeartbeatMs` | **Type**: int<br><br>**Description:** The requested heartbeat timeout in milliseconds. 0 means "heartbeats are disabled". Should be no lower than 1 second.<br><br>**Default**: `60000`                                                                                                             |

## Resilience

The RabbitMQ sink connector relies on its own RabbitMQ retry mechanism and doesn't include the configuration from [Resilience configuration](../settings.md#resilience-configuration).

## Broker Acknowledgment

By default, the connector waits for broker acknowledgment. Enabling broker acknowledgment ensures that each message sent
to RabbitMQ is confirmed by the broker before the publish operation is considered complete:

```json
{
  "WaitForBrokerAck": true
}
```

Disabling broker acknowledgment can significantly increase throughput by allowing the producer to continue sending
messages without waiting for confirmation from the broker. This is ideal for high-throughput scenarios where performance
is prioritized over delivery guarantees, despite a slight increase in the risk of message loss or duplication.

## Tutorial
[Learn how to set up and use a RabbitMQ Sink connector in KurrentDB through a tutorial.](/tutorials/RabbitMQ_Sink.md)
