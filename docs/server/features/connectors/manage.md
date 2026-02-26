---
title: 'Manage Connectors'
order: 4
---

This page offers a detailed list of operations for effectively managing your connectors.

## Create

Create a connector by sending a `POST` request to `connectors/{id}`, where `{id}` is a unique identifier of your choice for the connector.

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "name": "Example name",
  "settings": {
    "instanceTypeName": "{instanceTypeName}",
    "subscription:filter:scope": "stream",
    "subscription:filter:expression": "{stream-name}",
    "subscription:initialPosition": "earliest"
  }
}
```

When you start the connector using the [Start command](#start) and append an
event to the stream `{stream-name}`, the connector will consume and process the event
according to its configured sink type. Find out more about [Subscription configuration](./settings.md#subscription-configuration).

::: tip
The name field is optional and can be used to provide a human-readable name for the connector. If not provided, the connector will be named after the connector ID.
:::

## Start

Start a connector by sending a `POST` request to `connectors/{id}/start`, where `{id}` is the unique identifier used when the connector was created.

```http
POST /connectors/{id}/start
Host: localhost:2113
```

You can also start from a specific position by providing the start position in
the query parameter. Do this by sending a `POST` request to
`connectors/{id}/start/{log_position}` where `{log_position}` is the position
from which to start consuming events.

```http
POST /connectors/{id}/start/32789
Host: localhost:2113
```

::: note
If you do not provide a start position, the connector will start consuming
events from an existing checkpoint position, defaulting to the subscription
initial position if no checkpoint exists.
:::

## List

List all connectors by sending a `GET` request to `/connectors`.

```http
GET /connectors
Host: localhost:2113
```

### Query parameters

You can filter and paginate the results using the following query parameters:

| Parameter          | Type    | Description                                                    | Default |
| ------------------ | ------- | -------------------------------------------------------------- | ------- |
| `state`            | string  | Filter by connector state. Can be specified multiple times.    | -       |
| `instanceTypeName` | string  | Filter by instance type name. Can be specified multiple times. | -       |
| `connectorId`      | string  | Filter by connector ID. Can be specified multiple times.       | -       |
| `includeSettings`  | boolean | Include connector settings in the response.                    | `false` |
| `page`             | integer | Page number for pagination.                                    | `1`     |
| `pageSize`         | integer | Number of items per page.                                      | `100`   |

To specify multiple values for a parameter, repeat the query parameter:

```http
GET /connectors?state=CONNECTOR_STATE_RUNNING&state=CONNECTOR_STATE_STOPPED&instanceTypeName=serilog-sink
Host: localhost:2113
```

### State values

The `state` filter accepts the following values:

| State                          | Description                                              |
| ------------------------------ | -------------------------------------------------------- |
| `CONNECTOR_STATE_ACTIVATING`   | The connector is in the process of starting up.          |
| `CONNECTOR_STATE_RUNNING`      | The connector is actively running and consuming events.  |
| `CONNECTOR_STATE_DEACTIVATING` | The connector is in the process of shutting down.        |
| `CONNECTOR_STATE_STOPPED`      | The connector is stopped and not consuming events.       |

### Examples

Example with filters:

```http
GET /connectors?state=CONNECTOR_STATE_RUNNING&state=CONNECTOR_STATE_STOPPED&includeSettings=true
Host: localhost:2113
```

::: details Example response

```HTTP
HTTP/1.1 200 OK
Content-Type: application/json

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

## View settings

View the settings for a connector by sending a `GET` request to
`/connectors/{id}/settings`, where `{id}` is the unique
identifier used when the connector was created.

```http
GET /connectors/{id}/settings
Host: localhost:2113
```

::: details Example response

```HTTP
HTTP/1.1 200 OK
Content-Type: application/json

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

Reset a connector by sending a `POST` request to `/connectors/{id}/reset`, where `{id}` is the unique identifier used when the connector was created.

```http
POST /connectors/serilog-sink/reset
Host: localhost:2113
```

You can also reset the connector to a specific position by providing the reset
position in the query parameter. Do this by sending a `POST` request to
`/connectors/{id}/reset/{log_position}` where `{log_position}` is the position
to which the connector should be reset.

```http
POST /connectors/{id}/reset/25123
Host: localhost:2113
```

::: note
If no reset position is provided, the connector will reset the position to the beginning of the stream.
:::

## Stop

Stop a connector by sending a `POST` request to `/connectors/{id}/stop`, where `{id}` is the unique identifier used when the connector was created.

```http
POST /connectors/{id}/stop
Host: localhost:2113
```

## Reconfigure

Reconfigure an existing connector by sending a `PUT` request to
`/connectors/{id}/settings`, where `{id}` is the unique
identifier used when the connector was created. This endpoint allows you to
modify the settings of a connector without having to delete and recreate it.

```http
PUT /connectors/{id}/settings
Host: localhost:2113
Content-Type: application/json

{
  "instanceTypeName": "{instanceTypeName}",
  "logging:enabled": "false"
}
```

For a comprehensive list of available configuration options available for all sinks, please refer to the [Connector Settings](./settings.md) page.

::: note
The connector must be stopped before reconfiguring. If the connector is running,
the reconfigure operation will fail. Make sure to [Stop](#stop) the connector
before attempting to reconfigure it.
:::

## Delete

Delete a connector by sending a `DELETE` request to
`/connectors/{id}`, where `{id}` is the unique
identifier used when the connector was created.

```http
DELETE /connectors/serilog-sink
Host: localhost:2113
```

## Rename

To rename a connector, send a `PUT` request to
`/connectors/{id}/rename`, where `{id}` is the unique
identifier used when the connector was created.

```http
PUT /connectors/serilog-sink/rename
Host: localhost:2113
Content-Type: application/json

{
  "name": "New connector name"
}
```
