---
title: 'Elasticsearch Sink'
order: 1
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Elasticsearch sink retrieves messages from a KurrentDB stream and stores them in
an Elasticsearch index. Each record is serialized as a JSON document compatible with
Elasticsearch's document structure. 

## Quickstart

You can create the Elasticsearch Sink connector as follows. Replace `id` with a unique connector name or ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "elasticsearch-sink",
    "url": "http://localhost:9200",
    "indexName": "sample-index",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
```

After creating and starting the Elasticsearch sink connector, every time an
event is appended to the `example-stream`, the Elasticsearch sink connector will
send the record to the specified index in Elasticsearch. You can find a list of
available management API endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your
Elasticsearch sink connector with KurrentDB, ensuring it operates according to
your requirements and preferences.

::: tip
The Elasticsearch sink inherits a set of common settings that are used to
configure the connector. The settings can be found in the [Sink Options](../settings.md#sink-options) page.
:::

The Elasticsearch sink can be configured with the following options:

| Name                                        | Details                                                                                                                                                                                                                                                                                                                                                                                            |
| ------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `url`                                       | _required_<br><br>**Description:**<br>The URL of the Elasticsearch cluster to which the connector connects.<br><br>**Default**: `"http://localhost:9200"`                                                                                                                                                                                                                                          |
| `indexName`                                 | _required_<br><br>**Description:**<br>The index name to which the connector writes messages.<br><br>**Default**: `""`                                                                                                                                                                                                                                                                              |
| `refresh`                                   | **Description:**<br>Specifies whether Elasticsearch should refresh the affected shards to make this operation visible to search. If set to `"true"`, Elasticsearch refreshes the affected shards. If set to `"wait_for"`, Elasticsearch waits for a refresh to make this operation visible to search. If set to `"false"`, Elasticsearch does nothing with refreshes.<br><br>**Default**: `"true"` |
| `documentId:source`                         | **Description:**<br>The attribute used to generate the document id.<br><br>**Default**: `"recordId"`<br><br>**Accepted Values:** `"recordId"`, `"stream"`, `"headers"` or `"streamSuffix"`.                                                                                                                                                                                     |
| `documentId:expression`                     | **Description:**<br>The expression used to format the document id based on the selected source. This allows for custom id generation logic.<br><br>**Default**: `""`                                                                                                                                                                                                                               |
| `authentication:method`                     | **Description:**<br>The authentication method used by the connector to connect to the Elasticsearch cluster.<br><br>**Default**: `"basic"`<br><br>**Accepted Values:** `"basic"`, `"token"`, `"apiKey"`                                                                                                                                                                                            |
| `authentication:username`                   | _protected_<br><br>**Description:**<br>The username used by the connector to connect to the Elasticsearch cluster. If username is set, then password should also be provided.<br><br>**Default**: `"elastic"`                                                                                                                                                                                      |
| `authentication:password`                   | _protected_<br><br>**Description:**<br>The password used by the connector to connect to the Elasticsearch cluster. If username is set, then password should also be provided.<br><br>**Default**: `"changeme"`                                                                                                                                                                                     |
| `authentication:apiKey`                     | _protected_<br><br>**Description:**<br>The API key used by the connector to connect to the Elasticsearch cluster. Used if the method is set to ApiKey.<br><br>**Default**: `""`                                                                                                                                                                                                                    |
| `authentication:base64ApiKey`               | _protected_<br><br>**Description:**<br>The Base64 Encoded API key used by the connector to connect to the Elasticsearch cluster. Used if the method is set to Token or Base64ApiKey.<br><br>**Default**: `""`                                                                                                                                                                                      |
| `authentication:clientCertificate:rawData`  | _protected_<br><br>**Description:**<br>Base64 encoded x509 client certificate for Mutual TLS authentication with Elasticsearch.<br><br>**Default**: `""`                                                                                                                                                                                                                                           |
| `authentication:clientCertificate:password` | _protected_<br><br>**Description:**<br>The password for the client certificate, if required.<br><br>**Default**: `""`                                                                                                                                                                                                                                                                              |
| `authentication:rootCertificate:rawData`    | _protected_<br><br>**Description:**<br>Base64 encoded x509 root certificate for TLS authentication with Elasticsearch.<br><br>**Default**: `""`                                                                                                                                                                                                                                                    |
| `authentication:rootCertificate:password`   | _protected_<br><br>**Description:**<br>The password for the root certificate, if required.<br><br>**Default**: `""`                                                                                                                                                                                                                                                                                |
| `batching:batchSize`                        | **Type**: integer<br><br>**Description:**<br>Threshold batch size at which the sink will push the batch of records to the Elasticsearch index.<br><br>**Default**: `"1000"`                                                                                                                                                                                                                        |
| `batching:batchTimeoutMs`                   | **Type**: integer<br><br>**Description:**<br>Threshold time in milliseconds at which the sink will push the current batch of records to the Elasticsearch index, regardless of the batch size.<br><br>**Default**: `"250"`                                                                                                                                                                         |
| `resilience:enabled`                        | **Description:**<br>Enables resilience mechanisms for handling connection failures and retries.<br><br>**Default**: `"true"`                                                                                                                                                                                                                                                                       |
| `resilience:connectionLimit`                | **Type**: integer<br><br>**Description:**<br>The maximum number of concurrent connections to Elasticsearch.<br><br>**Default**: `"80"`                                                                                                                                                                                                                                                             |
| `resilience:maxRetries`                     | **Type**: integer<br><br>**Description:**<br>The maximum number of retries for a request.<br><br>**Default**: `"2147483647"`                                                                                                                                                                                                                                                                       |
| `resilience:maxRetryTimeout`                | **Type**: long<br><br>**Description:**<br>The maximum timeout in milliseconds for retries.<br><br>**Default**: `"60"`                                                                                                                                                                                                                                                                              |
| `resilience:requestTimeout`                 | **Type**: long<br><br>**Description:**<br>The timeout in milliseconds for a request.<br><br>**Default**: `"60000"`                                                                                                                                                                                                                                                                                 |
| `resilience:dnsRefreshTimeout`              | **Type**: long<br><br>**Description:**<br>The time in milliseconds after which to refresh the DNS information.<br><br>**Default**: `"300000"`                                                                                                                                                                                                                                                      |
| `resilience:pingTimeout`                    | **Type**: long<br><br>**Description:**<br>The timeout in milliseconds for a ping request.<br><br>**Default**: `"120000"`                                                                                                                                                                                                                                                                           |
| `resilience:retryOnErrorTypes`              | **Description:**<br>Comma-separated list of error types to retry on.<br><br>**Default**: `""`                                                                                                                                                                                                                                                                                                      |

Other resilience options can be found in the [Resilience Configuration](../settings.md#resilience-configuration) section.

## Authentication

The Elasticsearch sink connector supports multiple authentication methods.

### Basic Authentication

To use Basic Authentication, set the method to `basic` and provide the username and password:

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:method": "basic",
  "authentication:username": "elastic",
  "authentication:password": "your_secure_password"
}
```

### API Key Authentication

To use API Key authentication, set the method to `apiKey` and provide the API key:

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:method": "apiKey",
  "authentication:apiKey": "your_api_key"
}
```

### Token Authentication

To use Token authentication, set the method to `token` and provide the Base64 encoded API key:

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:method": "token",
  "authentication:base64ApiKey": ".."
}
```

### Certificate Authentication

To configure Mutual TLS authentication, provide the client and root certificates:

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "authentication:clientCertificate:rawData": "..",
  "authentication:clientCertificate:password": "..",
  "authentication:rootCertificate:rawData": "..",
  "authentication:rootCertificate:password": ".."
}
```

## Metadata

The Elasticsearch sink connector automatically includes these [default headers](../features.md#headers) in each document sent to Elasticsearch. These headers are stored in a `_metadata` field, which can be excluded from indexing through your Elasticsearch index configuration if needed.

## Document ID

The ID of the document can be generated automatically based on the source specified and expression if needed.

By default, the Elasticsearch sink uses the `recordId` as the document ID. This is the unique identifier generated for every record in KurrentDB.

If you opt not to use the default `recordId`, the following are alternative configuration options to generate document IDs based on different sources:

**Set Document ID using Stream ID**

You can extract part of the stream name using a regular expression (regex) to
define the document id. The expression is optional and can be customized based
on your naming convention. In this example, the expression captures the stream
name up to `_data`.

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "documentId:source": "stream",
  "documentId:expression": "^(.*)_data$"
}
```

Alternatively, if you only need the last segment of the stream name (after a
hyphen), you can use the `streamSuffix` source. This doesn't require an
expression since it automatically extracts the suffix.

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "documentId:source": "streamSuffix"
}
```

The `streamSuffix` source is useful when stream names follow a structured
format, and you want to use only the trailing part as the document ID. For
example, if the stream is named `user-123`, the document ID would be `123`.

**Set Document ID from Headers**

You can create the document ID by combining values from a record's metadata.

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "documentId:source": "headers",
  "documentId:expression": "key1,key2"
}
```

Specify the header keys you want to use in the `documentId:expression` field (e.g., `key1,key2`). The connector will concatenate the header values with a hyphen (`-`) to create the partition key.

For example, if your event has headers `key1: regionA` and `key2: zone1`, the partition key will be `regionA-zone1`.

## Resilience Configuration

The Elasticsearch sink provides extensive resilience options to handle connection issues and retry failed operations:

```http
PUT /connectors/{{id}}/settings
Host: localhost:2113
Content-Type: application/json

{
  "resilience:enabled": "true",
  "resilience:connectionLimit": "80",
  "resilience:maxRetries": "5",
  "resilience:requestTimeout": "30000",
  "resilience:retryOnErrorTypes": "timeout,server_error"
}
```

This configuration enables resilience, limits connections to 80, sets a maximum
of 5 retries, sets a request timeout of 30 seconds, and specifies that the
connector should retry on timeout and server error types.
