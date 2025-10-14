---
title: "MongoDB Sink"
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The MongoDB sink pulls messages from a KurrentDB stream and stores them in a
collection. The records will be serialized into
[BSON](https://www.mongodb.com/docs/manual/reference/glossary/#std-term-BSON)
documents, so the data must be valid for BSON format. 

## Quickstart

You can create the MongoDB Sink connector as follows. Replace `id` with a unique connector name or ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "mongo-db-sink",
    "connectionString": "mongodb://127.0.0.1:27020",
    "database": "my_database",
    "collection": "my_collection",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
```

After creating and starting the MongoDB sink connector, every time an event is
appended to the `example-stream`, the MongoDB sink connector will send the
record to the specified collection in the database. You can find a list of
available management API endpoints in the [API Reference](../manage.md).

## Settings

Adjust these settings to specify the behavior and interaction of your MongoDB sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The MongoDB sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The MongoDB sink can be configured with the following options:

| Name                      | Details                                                                                                                                                                                                          |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `database`                | _required_<br><br>**Description:**<br>The name of the database where the records will be stored.                                                                                                                 |
| `collection`              | _required_<br><br>**Description:**<br>The collection name that resides in the database to push records to.                                                                                                       |
| `connectionString`        | _required_<br><br>**Description:**<br>The MongoDB URI to which the connector connects.<br><br>**Default**: `"mongodb://mongoadmin:secret@localhost:27017/admin"`                                                 |
| `documentId:source`       | **Description:**<br>The attribute used to generate the document id.<br><br>**Default**: `"recordId"`<br><br>**Accepted Values:**<br>- `"recordId"`, `"stream"`, `"headers"`, `"streamSuffix"`, `"PartitionKey"`. |
| `documentId:expression`   | **Description:**<br>The expression used to format the document id based on the selected source. This allows for custom id generation logic.<br><br>**Default**: `"250"`                                          |
| `certificate:rawData`     | **Description:**<br>Base64 encoded x509 certificate.<br><br>**Default**: ""                                                                                                                                      |
| `certificate:password`    | **Description:**<br>The password used to access the x509 certificate for secure connections.<br><br>**Default**: ""                                                                                              |
| `batching:batchSize`      | **Description:**<br>Threshold batch size at which the sink will push the batch of records to the MongoDB collection.<br><br>**Default**: `"1000"`                                                                |
| `batching:batchTimeoutMs` | **Description:**<br>Threshold time in milliseconds at which the sink will push the current batch of records to the MongoDB collection, regardless of the batch size.<br><br>**Default**: `"250"`                 |

## Authentication

This MongoDB sink connector currently only supports [SCRAM](./mongo.md#scram) and [X.509 certificate authentication](./mongo.md#x509-certificate-authentication).

### SCRAM

To use SCRAM for authentication, include the username and password in the
connection string and set the `authMechanism` parameter in the connection string
to either `SCRAM-SHA-1` or `SCRAM-SHA-256` to select the desired MongoDB
authentication mechanism. For more explanations on the connection string URI
refer to the official MongoDB documentation on [Authentication Mechanism](https://www.mongodb.com/docs/v4.4/core/authentication-mechanisms/#:~:text=To%20specify%20the%20authentication%20mechanism,mechanism%20from%20the%20command%20line.).

::: note
MongoDB version 4.0 and later uses SCRAM-SHA-256 as the default authentication mechanism if the MongoDB server version supports it.
:::

### X.509 certificate authentication

To use X.509 certificate authentication, include the base64 encoded x509
certificate and the password in the settings. You can use an online tool like
[base64encode](https://www.base64encode.org/) to encode your certificate.

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "certificate:rawData": "base64encodedstring",
  "certificate:password": "password"
}
```

## Metadata

The MongoDB sink connector automatically includes these [default headers](../features.md#headers) in each document sent to the collection. These
headers provide metadata about the event and are stored in a `_metadata` field
within the document.

## Document ID

The id of the document can be generated automatically based on the source specified and expression if needed. The following options are available:

By default, the MongoDB sink uses the `recordId` as the document ID. This is the unique identifier generated for every record in KurrentDB.

Here are some examples that demonstrate how to configure the MongoDB sink to generate document IDs based on different sources.

**Set Document ID using Stream ID**

You can extract part of the stream name using a regular expression (regex) to
define the document id. The expression is optional and can be customized based
on your naming convention. In this example, the expression captures the stream
name up to `_data`.

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "documentId:source": "stream",
  "documentId:expression": "^(.*)_data$"
}
```

Alternatively, if you only need the last segment of the stream name (after a
hyphen), you can use the `streamSuffix` source. This
doesn't require an expression since it automatically extracts the suffix.

```http
POST /connectors/{{id}}
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

You can generate the document ID by concatenating values from specific event headers. In this case, two header values (`key1` and `key2`) are combined to form the ID.

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

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

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "key1": "value1",
  "key2": "value2"
}

// outputs "value1-value2"
```

:::

**4. Set Document ID from Partition Key**

If your event has a partition key, you can use it as the document ID. The `partitionKey` source directly uses this key without requiring an expression.

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "documentId:source": "partitionKey"
}
```

This uses the record's partition key as a unique document ID.

## Tutorial
[Learn how to set up and use a MongoDB Sink connector in KurrentDB through a tutorial.](/tutorials/MongoDB_Sink.md)
