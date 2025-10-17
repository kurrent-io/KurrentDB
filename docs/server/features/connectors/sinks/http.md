---
title: "HTTP Sink"
---

## Overview

The HTTP sink allows for integration between KurrentDB and external
APIs over HTTP or HTTPS. This connector consumes events from a KurrentDB
stream and converts each event's data into JSON format before sending it in the
request body to a specified Url. Events are sent individually as they are
consumed from the stream, without batching. The event data is transmitted as the
request body, and metadata can be included as HTTP headers. The connector also
supports Basic Authentication and Bearer Token Authentication. See [Authentication](#authentication).

## Quickstart

You can create the HTTP Sink connector as follows. Replace `id` with a unique connector name or ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "http-sink",
    "url": "https://api.example.com/",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
```

After creating and starting the HTTP sink connector, every time an event is
appended to the `example-stream`, the HTTP sink connector will send the record
to the specified URL. You can find a list of available management API endpoints
in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your HTTP sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The HTTP sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

### HTTP settings

| Name                       | Details                                                                                                                                                                                                      |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `url`                      | _required_<br><br> **Description:**<br>The URL or endpoint to which the request or message will be sent. See [Template Parameters](http#template-parameters) for advanced settings.<br><br>**Default**: `""` |
| `method`                   | **Description:**<br>The method or operation to use for the request or message.<br><br>**Default**: `"POST"`                                                                                                  |
| `defaultHeaders`           | **Description:**<br>Custom headers to include in the request. See [Specifying Custom Headers](#specifying-custom-headers).<br><br>**Default**: `"Accept-Encoding:*"`                                         |
| `pooledConnectionLifetime` | **Description:**<br>Maximum time a connection can stay in the pool before it is no longer reusable.<br><br>**Default**: `"00:05:00"` (5 minutes)                                                             |

### Authentication

The HTTP sink connector supports both [Basic](https://datatracker.ietf.org/doc/html/rfc7617) and [Bearer](https://datatracker.ietf.org/doc/html/rfc6750) authentication methods.

| Name                    | Details                                                                                                                                          |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `authentication:method` | **Description:**<br>The authentication method to use.<br><br>**Default**: `"None"`<br><br>**Accepted Values:** `"None"`,`"Basic"`, or `"Bearer"` |

#### Basic Authentication

| Name                            | Details                                                                             |
| ------------------------------- | ----------------------------------------------------------------------------------- |
| `authentication:basic:username` | **Description:**<br>The username for basic authentication.<br><br>**Default**: `""` |
| `authentication:basic:password` | **Description:**<br>The password for basic authentication.<br><br>**Default**: `""` |

#### Bearer Authentication

| Name                          | Details                                                                           |
| ----------------------------- | --------------------------------------------------------------------------------- |
| `authentication:bearer:token` | **Description:**<br>The token for bearer authentication.<br><br>**Default**: `""` |

#### Resilience

Resilience options can be found in the [Resilience Configuration](../settings.md#resilience-configuration) section.

## Headers

In addition to the [default headers](../features.md#headers) that are included with every request, the HTTP sink connector also includes the following headers in the HTTP request:

| Header              | Description                                                    |
| ------------------- | -------------------------------------------------------------- |
| `esdb-request-id`   | A unique identifier for the request                            |
| `esdb-request-date` | The timestamp when the request was processed                   |
| `content-type`      | The media type of the resource                                 |
| `accept-encoding`   | The encoding types the client can handle. Default value is `*` |

### Specifying Custom Headers

The HTTP sink connector lets you include custom headers in the HTTP requests it
sends to your specified URL. To add custom headers, use the `defaultHeaders`
setting in your connector configuration. Each custom header should be specified
with the prefix `defaultHeaders:` followed by the header name.

**Example Request**:

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

With this configuration, every HTTP request sent by the connector will include
the specified custom headers along with their respective values, in addition to
the [default headers](../features.md#headers) that are automatically added by
the connector's plugin.

## Template parameters

The HTTP sink supports the use of template parameters in the URL,
allowing for dynamic construction of the request URL based on event data. This
feature enables you to customize the destination URL for each event, making it
easier to integrate with APIs that require specific URL structures.

The following template parameters are available for use in the URL:

| Parameter          | Description                                                     |
| ------------------ | --------------------------------------------------------------- |
| `{schema-subject}` | The event's schema subject, converted to lowercase with hyphens |
| `{event-type}`     | Alias for `{schema-subject}`                                    |
| `{stream}`         | The KurrentDB stream ID                                         |

**Usage**

To use template parameters, include them in the `Url` option of your HTTP sink configuration. The parameters will be
replaced with their corresponding values for each event.

Example:

```
https://api.example.com/{schema-subject}
```

For an event with schema subject "TestEvent", this would result in the URL:

```
https://api.example.com/test-event
```

## Tutorial

[Learn how to configure and use a connector using the HTTP Sink in KurrentDB through a tutorial.](/tutorials/HTTP_Connector.md)
