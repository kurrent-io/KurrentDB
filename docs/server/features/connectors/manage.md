---
title: 'Manage Connectors'
order: 4
---

This page offers a detailed list of operations for effectively managing your connectors.

::: info
This page uses the `serilog-sink` connector as an example. Replace `instanceTypeName` with the unique identifier of your chosen connector. For more information on the instance type name, refer to the [individual documentation](./sinks/) for each connector.
:::

<template>
  <div>
    <label for="connector">Select Connector Type:</label>
    <select id="connector" v-model="selectedConnector">
      <option v-for="type in connectorTypes" :value="type">{{ type }}</option>
    </select>
  </div>
</template>

<script>
export default {
  data() {
    return {
      selectedConnector: "serilog-sink", // Default connector type
      connectorTypes: ["serilog-sink", "http-sink", "custom-sink"], // Add more connector types as needed
    };
  },
};
</script>

## Create

Create a connector by sending a `POST` request to `connectors/{connector_id}`, where `{connector_id}` is a unique identifier of your choice for the connector.

::: tabs
@tab Powershell

```powershell
$JSON = @"
{
  "name": "Demo Serilog Sink",
  "settings": {
    "instanceTypeName": "serilog-sink",
    "subscription:filter:scope": "stream",
    "subscription:filter:expression": "some-stream",
    "subscription:initialPosition": "earliest"
  }
}
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/serilog-sink
```

@tab Bash

```bash
JSON='{
  "name": "Demo Serilog Sink",
  "settings": {
    "instanceTypeName": "serilog-sink",
    "subscription:filter:scope": "stream",
    "subscription:filter:expression": "some-stream",
    "subscription:initialPosition": "earliest"
  }
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/serilog-sink
```

:::

When you start the connector using the [Start command](#start), and append an
event to the stream `some-stream`, the connector will consume the event and log
it to the console. Find out more about [Subscription configuration](./settings.md#subscription-configuration).

::: note
The name field is optional and can be used to provide a human-readable name for the connector. If not provided, the connector will be named after the connector ID.
:::

## Start

Start a connector by sending a `POST` request to `connectors/{connector_id}/start`, where `{connector_id}` is the unique identifier used when the connector was created.

::: tabs
@tab Powershell

```powershell
curl.exe -i -X POST http://localhost:2113/connectors/serilog-sink/start
```

@tab Bash

```bash
curl -i -X POST -H http://localhost:2113/connectors/serilog-sink/start
```

:::

You can also start from a specific position by providing the start position in
the query parameter. Do this by sending a `POST` request to
`connectors/{connector_id}/start/{log_position}` where `{log_position}` is the position
from which to start consuming events.

::: tabs
@tab Powershell

```powershell
curl.exe -i -X POST http://localhost:2113/connectors/serilog-sink/start/32789
```

@tab Bash

```bash
curl -i -X POST http://localhost:2113/connectors/serilog-sink/start/32789
```

:::

::: note
If you do not provide a start position, the connector will start consuming
events from an existing checkpoint position, defaulting to the subscription
initial position if no checkpoint exists.
:::

## List

List all connectors by sending a `GET` request to `/connectors`.

::: tabs
@tab Powershell

```powershell
$JSON = @"
{
  "state": ["CONNECTOR_STATE_STOPPED", "CONNECTOR_STATE_RUNNING"],
  "instanceTypeName": ["serilog-sink"],
  "connectorId": ["serilog-sink"],
  "includeSettings": true,
  "paging": {
      "page": 1,
      "pageSize": 100
  }
}
"@ `

curl.exe -X GET `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors
```

@tab Bash

```bash
JSON='{
  "state": ["CONNECTOR_STATE_STOPPED", "CONNECTOR_STATE_RUNNING"],
  "instanceTypeName": ["serilog-sink"],
  "connectorId": ["serilog-sink"],
  "includeSettings": "true",
  "paging": {
      "page": 1,
      "pageSize": 100
  }
}'

curl -X GET \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors
```

:::

You can paginate the results by specifying the `pageSize` and `page` parameters.
Additionally, you can filter the results using the `state`, `instanceType`, and
`connectorId` parameters.

::: details Click here to see a list example

```json
{
  "items": [
    {
      "connectorId": "http-sink",
      "name": "Demo HTTP Sink",
      "state": "CONNECTOR_STATE_STOPPED",
      "stateTimestamp": "2024-08-13T12:21:50.506102900Z",
      "settings": {
        "instanceTypeName": "http-sink",
        "url": "http://localhost:8080/sink",
        "transformer:Enabled": "true",
        "transformer:Function": "ZnVuY3Rpb24gdHJhbnNmb3JtKHJlY29yZCkgewogIGxldCB7IG1ha2UsIG1vZGVsIH0gPSByZWNvcmQudmFsdWUudmVoaWNsZTsKICByZWNvcmQuc2NoZW1hSW5mby5zdWJqZWN0ID0gJ1ZlaGljbGUnOwogIHJlY29yZC52YWx1ZS52ZWhpY2xlLm1ha2Vtb2RlbCA9IGAke21ha2V9ICR7bW9kZWx9YDsKfQ==",
        "subscription:filter:scope": "stream",
        "subscription:filter:expression": "^\\$connectors\\/[^\\/]+\\/leases"
      },
      "settingsTimestamp": "2024-08-13T12:21:50.506102900Z"
    },
    {
      "connectorId": "serilog-sink",
      "name": "Demo Serilog Sink",
      "state": "CONNECTOR_STATE_RUNNING",
      "stateTimestamp": "2024-08-13T12:21:47.459327600Z",
      "settings": {
        "instanceTypeName": "serilog-sink",
        "subscription:filter:scope": "stream",
        "subscription:filter:expression": "some-stream",
        "subscription:initialPosition": "earliest"
      },
      "settingsTimestamp": "2024-08-13T12:21:47.366197400Z",
      "position": 16829
    }
  ],
  "totalCount": 2,
  "paging": {
    "page": 1,
    "pageSize": 100
  }
}
```

:::

The following states are available:

| State                          | Description                                           |
| ------------------------------ | ----------------------------------------------------- |
| `CONNECTOR_STATE_UNKNOWN`      | The state of the connector is unknown.                |
| `CONNECTOR_STATE_ACTIVATING`   | The connector is in the process of being activated.   |
| `CONNECTOR_STATE_RUNNING`      | The connector is currently running.                   |
| `CONNECTOR_STATE_DEACTIVATING` | The connector is in the process of being deactivated. |
| `CONNECTOR_STATE_STOPPED`      | The connector is currently stopped.                   |

## View settings

View the settings for a connector by sending a `GET` request to
`/connectors/{connector_id}/settings`, where `{connector_id}` is the unique
identifier used when the connector was created.

::: tabs
@tab Powershell

```powershell
curl.exe -X GET http://localhost:2113/connectors/serilog-sink/settings
```

@tab Bash

```bash
curl -X GET http://localhost:2113/connectors/serilog-sink/settings
```

:::

::: details Click here to see an example of the settings

```json
{
  "settings": {
    "instanceTypeName": "serilog-sink",
    "subscription:filter:scope": "stream",
    "subscription:filter:expression": "some-stream",
    "subscription:initialPosition": "latest"
  },
  "settingsUpdateTime": "2024-08-14T18:12:16.500822500Z"
}
```

:::

## Reset

Reset a connector by sending a `POST` request to `/connectors/{connector_id}/reset`, where `{connector_id}` is the unique identifier used when the connector was created.

::: tabs
@tab Powershell

```powershell
curl.exe -i -X POST http://localhost:2113/connectors/serilog-sink/reset
```

@tab Bash

```bash
curl -i -X POST http://localhost:2113/connectors/serilog-sink/reset
```

:::

You can also reset the connector to a specific position by providing the reset
position in the query parameter. Do this by sending a `POST` request to
`/connectors/{connector_id}/reset/{log_position}` where `{log_position}` is the position
to which the connector should be reset.

::: tabs
@tab Powershell

```powershell
curl.exe -i -X POST http://localhost:2113/connectors/serilog-sink/reset/25123
```

@tab Bash

```bash
curl -i -X POST http://localhost:2113/connectors/serilog-sink/reset/25123
```

:::

::: note
If no reset position is provided, the connector will reset the position to the beginning of the stream.
:::

## Stop

Stop a connector by sending a `POST` request to `/connectors/{connector_id}/stop`, where `{connector_id}` is the unique identifier used when the connector was created.

::: tabs
@tab Powershell

```powershell
curl.exe -i -X POST http://localhost:2113/connectors/serilog-sink/stop
```

@tab Bash

```bash
curl -i -X POST http://localhost:2113/connectors/serilog-sink/stop
```

:::

## Reconfigure

Reconfigure an existing connector by sending a `PUT` request to
`/connectors/{connector_id}/settings`, where `{connector_id}` is the unique
identifier used when the connector was created. This endpoint allows you to
modify the settings of a connector without having to delete and recreate it.

::: tabs
@tab Powershell

```powershell
$JSON = @"
{
  "instanceTypeName": "serilog-sink",
  "logging:enabled": "false"
}
"@ `

curl.exe -X PUT `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/serilog-sink/settings
```

@tab Bash

```bash
JSON='{
  "instanceTypeName": "serilog-sink",
  "logging:Enabled": "false"
}'

curl -X PUT \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/serilog-sink/settings
```

:::

For a comprehensive list of available configuration options available for all sinks, please refer to the [Connector Settings](./settings.md) page.

::: note
The connector must be stopped before reconfiguring. If the connector is running,
the reconfigure operation will fail. Make sure to [Stop](#stop) the connector
before attempting to reconfigure it.
:::

## Delete

Delete a connector by sending a `DELETE` request to
`/connectors/{connector_id}`, where `{connector_id}` is the unique
identifier used when the connector was created.

::: tabs
@tab Powershell

```powershell
curl.exe -X DELETE http://localhost:2113/connectors/serilog-sink
```

@tab Bash

```bash
curl -X DELETE http://localhost:2113/connectors/serilog-sink
```

:::

## Rename

To rename a connector, send a `PUT` request to
`/connectors/{connector_id}/rename`, where `{connector_id}` is the unique
identifier used when the connector was created.

::: tabs
@tab Powershell

```powershell
$JSON = @"
{
  "name": "New connector name"
}
"@ `

curl.exe -X PUT `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/serilog-sink/rename
```

@tab Bash

```bash
JSON='{
  "name": "New connector name"
}'

curl -X PUT \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/serilog-sink/rename
```

:::
