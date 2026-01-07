# User defined indexes

KurrentDB v26.0 introduces support for user defined indexes, building on the [secondary indexes](./secondary.md) mechanism added in v25.1.

## Introduction

User defined indexes allow you to create custom indexes based on record content, enabling efficient queries and subscriptions filtered by fields within your records — for example, retrieving all orders by country or users by region.

Indexes subscribe to the log and maintain index data on each node, separate from the log. This design avoids increasing the size of the log while providing fast, targeted access to records matching your criteria.

You can read from and subscribe to user defined indexes using the existing gRPC clients, or query them directly in the UI.

A user defined index can include:

- **A filter** - A JavaScript function that determines which records are added to the index. Only records where the filter returns `true` are indexed.
- **A field selector** *(optional)* - A JavaScript function that extracts a value from each matching record to store in the index entry. You can then use these field values to filter reads, subscriptions, and queries.

## Managing

User defined indexes can be managed through gRPC (coming to clients soon) and HTTP. See the
[API definition](https://github.com/kurrent-io/KurrentDB/blob/release/v26.0/proto/kurrentdb/protocol/v2/indexes/service.proto)

Admin or Operations permissions are required to create/start/stop/delete indexes. Any authenticated user can list/get indexes.

### Create

`POST` to `<host>:<port>/v2/indexes/<index-name>`

e.g. with the default admin credentials:
```http
POST https://127.0.0.1:2113/v2/indexes/orders-by-country
Content-Type: application/json
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=

{
  "filter": "rec => rec.schema.name == 'OrderCreated'",
  "fields": [{
    "name": "country",
      "selector": "rec => rec.value.country",
      "type": "INDEX_FIELD_TYPE_STRING"
    }]
}
```

`Filter`:
- The `filter` is optional. If not provided then all user records are included in the index.
- If provided, the `filter` must be deterministic based on the content of the record.
- If provided, the `filter` must return a boolean value. Returning `false` causes the record to be excluded from the index.
- If the `filter` does not return a boolean value, the record will be excluded from the index and an error logged.

`Fields`:
- Currently there can be at most 1 `field`. Future versions will allow multiple fields.
- The field `name` determines how the field will be read/subscribed/queried.
- The `selector` must be deterministic based on the content of the record.
- The `selector` can return `skip` to exclude the record from the index. This is an alternative filtering mechanism to the `filter` function. They can both be used.
- The `selector` must return a value compatible with the field `type` (or return `skip`). Otherwise the record will be excluded from the index and an error logged.

The available field types are:

```
INDEX_FIELD_TYPE_STRING
INDEX_FIELD_TYPE_DOUBLE
INDEX_FIELD_TYPE_INT_32
INDEX_FIELD_TYPE_INT_64
```

By default a user defined index will start automatically. This can be prevented by passing `"start": false` in the request.

The structure of the record passed to the `filter` and `selector` functions is:

```json
{
  "id": "12345678-1234-1234-1234-123456789abc", // the event ID
  "timestamp": "2026-01-15T13:37:01.337Z", // the time at which the event was written to the transaction log
  "position": {
    "stream": "my-stream", // the stream name
    "streamRevision": 2, // the event number
    "logPosition": 2705 // the commit position of the record in the transaction log
  },
  "schema": {
    "name": "my-event-type", // the event type
    "format": "Json" // the data format
  },
  "sequence": 3, // a sequence number that auto-increments each time a record is passed to the filter
  "redacted": false, // whether the record is redacted or not
  "value": { // deserialized data
    "my": "data"
  },
  "properties": { // deserialized metadata
    "my": "metadata"
  }
}
```

Now if you append a record representing a `OrderCreated` event with a payload like

```json
{
  "orderId": "ORD-1234",
  "country": "Mauritius",
  "total": 149.99
}
```

Then it will be indexed accordingly.

### Start

User defined indexes are started by default when they are created. If the create request specified not to start the index, or the index has been stopped, then it can be started as follows:

`POST` to `<host>:<port>/v2/indexes/<index-name>/start`

e.g. with the default admin credentials:
```http
POST https://127.0.0.1:2113/v2/indexes/orders-by-country/start
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

### Stop

`POST` to `<host>:<port>/v2/indexes/<index-name>/stop`

e.g. with the default admin credentials:
```http
POST https://127.0.0.1:2113/v2/indexes/orders-by-country/stop
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

### Delete

`DELETE` to `<host>:<port>/v2/indexes/<index-name>`

e.g. with the default admin credentials:
```http
DELETE https://127.0.0.1:2113/v2/indexes/orders-by-country
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

If deleting an index and recreating with the same name, be aware of consumers which have consumed or partially consumed the old index.

### List

`GET` from `<host>:<port>/v2/indexes/`

e.g. with the default admin credentials:
```http
GET https://127.0.0.1:2113/v2/indexes/
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

### Get

`GET` from `<host>:<port>/v2/indexes/<index-name>`

e.g. with the default admin credentials:
```http
GET https://127.0.0.1:2113/v2/indexes/orders-by-country
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

## Reading and subscribing

User defined indexes can be read and subscribed to via the filtered $all API very similarly to the built in [secondary indexes](./secondary.md#using-secondary-indexes).

The whole index can be consumed by using the stream prefix filter `$idx-user-<index-name>` e.g. `"$idx-user-orders-by-country"`

A particular field value can be selected by using the stream prefix filter `$idx-user-<index-name>:<field-value>` e.g. `"$idx-user-orders-by-country:Mauritius"`.

## Querying

User defined indexes can be queried in the Query UI (e.g. `https://localhost:2113/ui/query`)

e.g.
```sql
select * from index:orders-by-country where field_country='Mauritius' limit 10
```

## Monitoring

Metrics in updated grafana dashboard soon.

## Configuration

User defined indexes are enabled as part of secondary indexing, which is enabled by default but can be disabled in the server configuration:

```yaml
SecondaryIndexing:
  Enabled: false
```

Refer to the [configuration guide](../../configuration/configuration.md) for configuration mechanisms other than YAML.

Note that on a large database the secondary indexes may take a while to build.

## Future work

The following are improvements we are considering for future versions:

- Multiple fields per index
- Updating an index definition
- Other filter/selector types e.g. json path.
