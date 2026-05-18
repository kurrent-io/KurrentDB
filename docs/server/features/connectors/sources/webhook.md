---
title: "Webhook Source"
---

## Overview

The Webhook Source connector lets external systems deliver webhook calls
directly to KurrentDB. Each incoming payload is written as an event to a
KurrentDB stream, with no middleware required in between.

## Prerequisites

Before using the Webhook Source connector, ensure you have:

- A KurrentDB instance with connectors enabled
- A data protection token configured in your KurrentDB instance (required to encrypt sensitive fields like the HMAC signature secret)
- A publicly reachable KurrentDB endpoint if the upstream provider sends webhooks from outside your network

::: tip
See the [Data Protection](../features.md#data-protection) documentation for instructions on configuring the encryption token.
:::

## Quickstart

You can create the Webhook Source connector as follows. Replace `{id}` with your desired connector ID.

The Webhook Source requires a `routingScript`: a base64-encoded UTF-8
JavaScript snippet defining a `route(request)` function. For each inbound
request, the function returns the target stream name and optionally a
schema name to tag the event with.

For example, the following function:

```js
function route(request) {
  return {
    stream: `webhook-${request.body.resource}-${request.body.action}`,
    schema: `${request.body.resource}.${request.body.action}`,
  };
}
```

Base64-encoded, this becomes:

```text
ZnVuY3Rpb24gcm91dGUocmVxdWVzdCkgeyByZXR1cm4geyBzdHJlYW06IGB3ZWJob29rLSR7cmVxdWVzdC5ib2R5LnJlc291cmNlfS0ke3JlcXVlc3QuYm9keS5hY3Rpb259YCwgc2NoZW1hOiBgJHtyZXF1ZXN0LmJvZHkucmVzb3VyY2V9LiR7cmVxdWVzdC5ib2R5LmFjdGlvbn1gIH07IH0=
```

Create the connector with:

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "webhook-source",
    "routingScript": "ZnVuY3Rpb24gcm91dGUocmVxdWVzdCkgeyByZXR1cm4geyBzdHJlYW06IGB3ZWJob29rLSR7cmVxdWVzdC5ib2R5LnJlc291cmNlfS0ke3JlcXVlc3QuYm9keS5hY3Rpb259YCwgc2NoZW1hOiBgJHtyZXF1ZXN0LmJvZHkucmVzb3VyY2V9LiR7cmVxdWVzdC5ib2R5LmFjdGlvbn1gIH07IH0="
  }
}
```

After creating and starting the connector, send JSON payloads to the webhook
endpoint:

```http
POST /webhook/{id}
Host: localhost:2113
Content-Type: application/json

{
  "resource": "order",
  "action": "created",
  "data": { "orderId": "abc-123" }
}
```

The example above writes the payload to stream `webhook-order-created` with
event type `order.created`. By default, the connector returns `202 Accepted`
as soon as it accepts the payload for processing. You can find a list of
available management API endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to control how the Webhook Source connector writes
inbound requests to KurrentDB.

::: tip
The Webhook Source inherits common settings shared by all source connectors,
documented on the [Source Options](../settings.md#source-options) page.
:::

| Name                            | Details                                                                                                                                                                                                                                                                                                                |
|---------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `routingScript`                 | _required_<br><br>**Description:**<br>Base64-encoded UTF-8 JavaScript that defines a `route(request)` function. The function returns the target stream name and an optional schema name for each inbound request. See [Routing Script](#routing-script).                                                              |
| `waitForWrite`                  | **Description:**<br>When enabled, the HTTP request waits for the event to be durably written before returning a response. See [Write Confirmation](#write-confirmation).<br><br>**Default**: `"false"`                                                                                                                 |
| `confirmationTimeout`           | **Description:**<br>How long the request waits for a durable-write confirmation when `waitForWrite` is enabled. Exceeding this returns `504 Gateway Timeout`.<br><br>**Default**: `"00:00:30"` (30 seconds)                                                                                                            |
| `signature:scheme`              | **Description:**<br>Pre-configured provider scheme for HMAC signature validation. Required when the `signature` block is present; you must pick a provider explicitly. Each scheme fully defines the header name, encoding, payload shape, and replay-protection behavior expected by that provider. See [Signature Validation](#signature-validation).<br><br>**Accepted Values:** `"GitHub"`, `"Shopify"`, `"Slack"`, `"Stripe"`<br><br>**Default**: not set (must be specified) |
| `signature:secret`              | **Description:**<br>Shared HMAC secret, interpreted as UTF-8. When the `signature` block is present, this must be a non-empty string. When the `signature` block is omitted entirely, signature validation is disabled.<br><br>**Default**: not set (validation disabled)                                              |
| `signature:timestampTolerance`  | **Description:**<br>Maximum allowed clock skew when the chosen scheme requires timestamp validation (Slack, Stripe). Ignored for schemes that do not include a timestamp.<br><br>**Default**: `"00:05:00"` (5 minutes)                                                                                                 |
| `allowedHeaders`                | **Description:**<br>Allowlist of inbound HTTP header names (case-insensitive) persisted on the written event. Each allowed header becomes its own metadata entry on the record, keyed by its lowercased name (e.g. `content-type`). Anything not on this list is never stored. Webhook providers routinely send headers that carry credentials (`Authorization`, `Cookie`, API keys), so only add headers you have verified do not contain secrets.<br><br>**Default**: `["Content-Type", "User-Agent", "X-Request-Id"]` |

## Ingest Endpoint

```http
POST /webhook/{connectorId}
```

The Webhook Source connector accepts `POST` requests only. Each request must use
a JSON content type such as `application/json` (including `application/json;
charset=utf-8`) and contain a non-empty UTF-8 JSON payload. The JSON payload
becomes the event data written to KurrentDB.

| Response                      | When it is returned                                                                                                                       |
|-------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------|
| `202 Accepted`                | The payload was accepted into the connector pipeline. This is the default behavior.                                                       |
| `201 Created`                 | `waitForWrite` is enabled and the event was durably written.                                                                              |
| `400 Bad Request`             | The JSON payload is empty or is not valid UTF-8 JSON, or the routing script returned `null`/an empty stream name (request was skipped).   |
| `401 Unauthorized`            | Signature validation is enabled and the signature header is missing, malformed, or the timestamp is outside the tolerance window.         |
| `404 Not Found`               | No active connector with that ID exists on the current node.                                                                              |
| `415 Unsupported Media Type`  | The request does not use a JSON content type.                                                                                             |
| `500 Internal Server Error`   | The routing script threw, timed out, returned a non-object value, or returned an invalid `stream`/`schema`. See [Routing Script](#routing-script). |
| `503 Service Unavailable`     | The connector could not accept the payload, or it stopped while a `waitForWrite` request was waiting for confirmation.                    |
| `504 Gateway Timeout`         | `waitForWrite` is enabled and the write did not complete within `confirmationTimeout`.                                                    |

## Routing Script

The routing script is a base64-encoded UTF-8 JavaScript snippet that must
define a function named `route`. The function is invoked once per inbound
request to compute the target stream and (optionally) the schema name.

### Request shape

The function receives a single `request` argument with the following fields:

| Field     | Type     | Description                                                                  |
|-----------|----------|------------------------------------------------------------------------------|
| `body`    | any      | Parsed JSON payload (object, array, or primitive).                           |
| `headers` | object   | Inbound HTTP headers, keyed by lowercased name with string values.           |
| `path`    | string   | Request path, e.g. `"/webhook/my-connector"`.                                |
| `query`   | object   | Parsed query string, keyed by name with string values.                       |

Header keys are always lowercased before they are passed to the script.

### Return value

The function must return an object containing a `stream` property, which is
the target stream name to write the event to. The stream must be a non-empty
string and must not start with `$`. An optional `schema` property tags the
event with a custom event type; when omitted, events are written with the
default event type `WebhookReceived`.

### Routing outcomes

The function's return value determines what happens to the request. To
route a request, return an object with a non-empty `stream` (and optionally
a `schema`); the request is written to that stream. To skip a request,
return `null`, `undefined`, or an object with an empty `stream`, and the
connector responds with `400 Bad Request` without writing an event. If the
function throws, runs too long, returns something other than an object, or
returns an invalid `stream` or `schema`, the connector responds with
`500 Internal Server Error`.

### Example

Route Stripe events to per-account streams and tag each event with its Stripe
event type:

```js
function route(request) {
  return {
    stream: `stripe-${request.body.account}`,
    schema: `stripe.${request.body.type}`,
  };
}
```

## Write Confirmation

By default, the connector returns `202 Accepted` as soon as the payload
enters the in-memory pipeline. A `202` confirms acceptance for processing,
not durable persistence. Enable `waitForWrite` to receive `201 Created`
only once the event has been durably written.

## Signature Validation

When a `signature` block is configured, the connector validates the HMAC-SHA256
signature of every incoming request using the rules of the chosen `scheme`.
Requests with missing, malformed, or invalid signatures are rejected with
`401 Unauthorized`. For schemes that include a timestamp, requests outside the
configured tolerance window are also rejected with `401 Unauthorized`.

Each scheme fully defines the header name, encoding, signed-payload shape, and
replay-protection behavior expected by that provider, so you only need to pick
the scheme and supply the secret.

| Scheme    | Header(s)                                                            | Encoding | Signed Payload          | Timestamp Validation |
|-----------|----------------------------------------------------------------------|----------|-------------------------|----------------------|
| `GitHub`  | `X-Hub-Signature-256` (with `sha256=` prefix)                        | Hex      | `body`                  | No                   |
| `Shopify` | `X-Shopify-Hmac-Sha256`                                              | Base64   | `body`                  | No                   |
| `Slack`   | `X-Slack-Signature` (with `v0=` prefix), `X-Slack-Request-Timestamp` | Hex      | `v0:{timestamp}:{body}` | Yes                  |
| `Stripe`  | `Stripe-Signature` (parsed pairs `t=…,v1=…`)                         | Hex      | `{timestamp}.{body}`    | Yes                  |

### GitHub

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "webhook-source",
    "routingScript": "<base64-encoded route() function>",
    "signature": {
      "scheme": "GitHub",
      "secret": "your-github-webhook-secret"
    }
  }
}
```

### Shopify

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "webhook-source",
    "routingScript": "<base64-encoded route() function>",
    "signature": {
      "scheme": "Shopify",
      "secret": "your-shopify-secret"
    }
  }
}
```

### Slack

Slack signs `v0:{timestamp}:{body}` and requires the timestamp header to be
within the tolerance window:

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "webhook-source",
    "routingScript": "<base64-encoded route() function>",
    "signature": {
      "scheme": "Slack",
      "secret": "your-slack-signing-secret",
      "timestampTolerance": "00:05:00"
    }
  }
}
```

### Stripe

Stripe sends a single `Stripe-Signature` header containing comma-separated
pairs (`t=…,v1=…`). The signed payload is `{timestamp}.{body}`:

```http
POST /connectors/{id}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "webhook-source",
    "routingScript": "<base64-encoded route() function>",
    "signature": {
      "scheme": "Stripe",
      "secret": "your-stripe-webhook-secret",
      "timestampTolerance": "00:05:00"
    }
  }
}
```

