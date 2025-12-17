# User defined indexes

KurrentDB v26.0 introduces support for user defined indexes, building on the [secondary indexes](./secondary.md) mechanism added in v25.1.

## Introduction

Secondary indexes run inside the KurrentDB server nodes using a `$all` catchup subscription to consume the log and produce index records into a `DuckDB` database kept on each node. They do not add write amplification to the main log. 

The indexes can be subscribed to and read from using the existing filtered $all API, or queried in the UI. Details of this below.

User defined indexes in v26.0 can include a javascript filter to determine which log records are added to the index and, optionally, a javascript field selector which will select a value from each record passing the filter to be stored in that record's index entry. The values of the fields can then be used to filter the output of the reads/subscriptions/queries.

## Enabling

User defined indexes are enabled as part of secondary indexing, which is enabled by default but can be disabled in the server configuration:

```yaml
SecondaryIndexing:
  Enabled: false
```

Refer to the [configuration guide](../../configuration/configuration.md) for configuration mechanisms other than YAML.

Note that on a large database the secondary indexes may take a while to build.

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
  "filter": "rec => rec.type == 'OrderCreated'",
  "fields": [{
    "name": "country",
      "selector": "rec => rec.data.country",
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
- In v26.0.0 there must be 0 or 1 `"fields"`.
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

The structure of the record passed to the `filter` and `selector` functions is currently:

```
rec.stream - stream id
rec.number - event number
rec.type - event type
rec.id - event id
rec.data - event data
rec.metadata - event metadata
rec.isJson - whether the data is json or not
```

::: note
The above describes the structure in `v26.0.0-rc.1` we are looking at providing the same structure provided to the javascript functions in `connectors` and this will likely be implemented before `v26.0.0` RTM.
:::

Now if you append an event of type `OrderCreated` with a payload like

```json
{
  "country": "Mauritius"
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

## Future work

The following are improvements we are considering for future versions:

- Multiple fields per index
- Updating an index definition
- Other filter/selector types e.g. json path.
