---
title: 'Elasticsearch Sink'
order: 1
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The Elasticsearch sink pulls messages from a KurrentDB stream and stores them in
an Elasticsearch index. The records will be serialized into JSON documents,
compatible with Elasticsearch's document structure.

## Quickstart

You can create the Elasticsearch Sink connector as follows:

::: tabs
@tab Powershell

```powershell
$JSON = @"
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
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/elasticsearch-sink-connector
```

@tab Bash

```bash
JSON='{
  "settings": {
    "instanceTypeName": "elasticsearch-sink",
    "url": "http://localhost:9200",
    "indexName": "sample-index",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/elasticsearch-sink-connector
```

:::

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

| Name                                        | Details                                                                                                                                                                                                                                                                                                                                                                                                         |
| ------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `url`                                       | _required_<br><br>**Type**: string<br><br>**Description:** The URL of the Elasticsearch cluster to which the connector connects.<br><br>**Default**: `http://localhost:9200`                                                                                                                                                                                                                                    |
| `indexName`                                 | _required_<br><br>**Type**: string<br><br>**Description:** The index name to which the connector writes messages.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                                                                         |
| `refresh`                                   | **Type**: string<br><br>**Description:** Specifies whether Elasticsearch should refresh the affected shards to make this operation visible to search. If set to `true`, Elasticsearch refreshes the affected shards. If set to `wait_for`, Elasticsearch waits for a refresh to make this operation visible to search. If set to `false`, Elasticsearch does nothing with refreshes.<br><br>**Default**: `true` |
| `documentId:source`                         | **Type**: string<br><br>**Description:** The attribute used to generate the document id.<br><br>**Default**: `recordId`<br><br>**Accepted Values:**<br>- `recordId`, `stream`, `headers`, `streamSuffix`, `partitionKey`.                                                                                                                                                                                       |
| `documentId:expression`                     | **Type**: string<br><br>**Description:** The expression used to format the document id based on the selected source. This allows for custom id generation logic.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                          |
| `authentication:method`                     | **Type**: string<br><br>**Description:** The authentication method used by the connector to connect to the Elasticsearch cluster.<br><br>**Default**: `basic`<br><br>**Accepted Values:**<br>- `basic`, `token`, `apiKey`                                                                                                                                                                                       |
| `authentication:username`                   | _protected_<br><br>**Type**: string<br><br>**Description:** The username used by the connector to connect to the Elasticsearch cluster. If username is set, then password should also be provided.<br><br>**Default**: `elastic`                                                                                                                                                                                |
| `authentication:password`                   | _protected_<br><br>**Type**: string<br><br>**Description:** The password used by the connector to connect to the Elasticsearch cluster. If username is set, then password should also be provided.<br><br>**Default**: `changeme`                                                                                                                                                                               |
| `authentication:apiKey`                     | _protected_<br><br>**Type**: string<br><br>**Description:** The API key used by the connector to connect to the Elasticsearch cluster. Used if the method is set to ApiKey.<br><br>**Default**: `` (empty string)                                                                                                                                                                                               |
| `authentication:base64ApiKey`               | _protected_<br><br>**Type**: string<br><br>**Description:** The Base64 Encoded API key used by the connector to connect to the Elasticsearch cluster. Used if the method is set to Token or Base64ApiKey.<br><br>**Default**: `` (empty string)                                                                                                                                                                 |
| `authentication:clientCertificate:rawData`  | _protected_<br><br>**Type**: string<br><br>**Description:** Base64 encoded x509 client certificate for Mutual TLS authentication with Elasticsearch.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                                      |
| `authentication:clientCertificate:password` | _protected_<br><br>**Type**: string<br><br>**Description:** The password for the client certificate, if required.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                                                                         |
| `authentication:rootCertificate:rawData`    | _protected_<br><br>**Type**: string<br><br>**Description:** Base64 encoded x509 root certificate for TLS authentication with Elasticsearch.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                                               |
| `authentication:rootCertificate:password`   | _protected_<br><br>**Type**: string<br><br>**Description:** The password for the root certificate, if required.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                                                                           |
| `batching:batchSize`                        | **Type**: integer<br><br>**Description:** Threshold batch size at which the sink will push the batch of records to the Elasticsearch index.<br><br>**Default**: `1000`                                                                                                                                                                                                                                          |
| `batching:batchTimeoutMs`                   | **Type**: integer<br><br>**Description:** Threshold time in milliseconds at which the sink will push the current batch of records to the Elasticsearch index, regardless of the batch size.<br><br>**Default**: `250`                                                                                                                                                                                           |
| `resilience:enabled`                        | **Type**: boolean<br><br>**Description:** Enables resilience mechanisms for handling connection failures and retries.<br><br>**Default**: `true`                                                                                                                                                                                                                                                                |
| `resilience:connectionLimit`                | **Type**: integer<br><br>**Description:** The maximum number of concurrent connections to Elasticsearch.<br><br>**Default**: `80`                                                                                                                                                                                                                                                                               |
| `resilience:maxRetries`                     | **Type**: integer<br><br>**Description:** The maximum number of retries for a request.<br><br>**Default**: `int.MaxValue`                                                                                                                                                                                                                                                                                       |
| `resilience:maxRetryTimeout`                | **Type**: long<br><br>**Description:** The maximum timeout in milliseconds for retries.<br><br>**Default**: `60`                                                                                                                                                                                                                                                                                                |
| `resilience:requestTimeout`                 | **Type**: long<br><br>**Description:** The timeout in milliseconds for a request.<br><br>**Default**: `60000`                                                                                                                                                                                                                                                                                                   |
| `resilience:dnsRefreshTimeout`              | **Type**: long<br><br>**Description:** The time in milliseconds after which to refresh the DNS information.<br><br>**Default**: `300000`                                                                                                                                                                                                                                                                        |
| `resilience:pingTimeout`                    | **Type**: long<br><br>**Description:** The timeout in milliseconds for a ping request.<br><br>**Default**: `120000`                                                                                                                                                                                                                                                                                             |
| `resilience:retryOnErrorTypes`              | **Type**: string<br><br>**Description:** Comma-separated list of error types to retry on.<br><br>**Default**: `` (empty string)                                                                                                                                                                                                                                                                                 |

## Authentication

The Elasticsearch sink connector supports multiple authentication methods.

### Basic Authentication

To use Basic Authentication, set the method to `basic` and provide the username and password:

```json
{
  "authentication:method": "basic",
  "authentication:username": "elastic",
  "authentication:password": "your_secure_password"
}
```

### API Key Authentication

To use API Key authentication, set the method to `apiKey` and provide the API key:

```json
{
  "authentication:method": "apiKey",
  "authentication:apiKey": "your_api_key"
}
```

### Token Authentication

To use Token authentication, set the method to `token` and provide the Base64 encoded API key:

```json
{
  "authentication:method": "token",
  "authentication:base64ApiKey": "your_base64_encoded_api_key"
}
```

### Certificate Authentication

To configure Mutual TLS authentication, provide the client and root certificates:

```json
{
  "authentication:clientCertificate:rawData": "base64_encoded_client_certificate",
  "authentication:clientCertificate:password": "certificate_password",
  "authentication:rootCertificate:rawData": "base64_encoded_root_certificate",
  "authentication:rootCertificate:password": "certificate_password"
}
```

## Document ID

The ID of the document can be generated automatically based on the source specified and expression if needed.

By default, the Elasticsearch sink uses the `recordId` as the document ID. This is the unique identifier generated for every record in KurrentDB.

If you opt not to use the default `recordId`, the following are alternative configuration options to generate document IDs based on different sources:

**Set Document ID using Stream ID**

You can extract part of the stream name using a regular expression (regex) to
define the document id. The expression is optional and can be customized based
on your naming convention. In this example, the expression captures the stream
name up to `_data`.

```json
{
  "documentId:source": "stream",
  "documentId:expression": "^(.*)_data$"
}
```

Alternatively, if you only need the last segment of the stream name (after a
hyphen), you can use the `streamSuffix` source. This doesn't require an
expression since it automatically extracts the suffix.

```json
{
  "documentId:source": "streamSuffix"
}
```

The `streamSuffix` source is useful when stream names follow a structured
format, and you want to use only the trailing part as the document ID. For
example, if the stream is named `user-123`, the document ID would be `123`.

**Set Document ID from Headers**

You can generate the document ID by concatenating values from specific event
headers. In this case, two header values (`key1` and `key2`) are combined to
form the ID.

```json
{
  "documentId:source": "headers",
  "documentId:expression": "key1,key2"
}
```

The `Headers` source allows you to pull values from the event's metadata. The
`documentId:expression` field lists the header keys (in this case, `key1` and
`key2`), and their values are concatenated to generate the document ID. This is
useful when headers hold important metadata that should define the document's
unique identifier, such as region, user ID, or other identifiers.

::: details Click here to see an example

```json
{
  "key1": "value1",
  "key2": "value2"
}

// outputs "value1-value2"
```

:::

**Set Document ID from Partition Key**

If your event has a partition key, you can use it as the document ID. The
`partitionKey` source directly uses this key without requiring an expression.

```json
{
  "documentId:source": "partitionKey"
}
```

This uses the record's partition key as a unique document ID.

## Resilience Configuration

The Elasticsearch sink provides extensive resilience options to handle connection issues and retry failed operations:

```json
{
  "resilience:enabled": true,
  "resilience:connectionLimit": 80,
  "resilience:maxRetries": 5,
  "resilience:requestTimeout": 30000,
  "resilience:retryOnErrorTypes": "timeout,server_error"
}
```

This configuration enables resilience, limits connections to 80, sets a maximum
of 5 retries, sets a request timeout of 30 seconds, and specifies that the
connector should retry on timeout and server error types.
