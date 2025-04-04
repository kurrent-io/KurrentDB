---
order: 1
---

# System projections

KurrentDB ships with five built in projections:

- [By Category](#by-category) (`$by_category`)
- [By Event Type](#by-event-type) (`$by_event_type`)
- [By Correlation ID](#by-correlation-id) (`$by_correlation_id`)
- [Stream by Category](#stream-by-category) (`$stream_by_category`)
- [Streams](#streams-projection) (`$streams`)

## Enabling system projections

When you start KurrentDB from a fresh database, these projections are present but disabled and querying
their statuses returns `Stopped`. You can enable a projection by issuing a request which switches the status
of the projection from `Stopped` to `Running`.

```bash:no-line-numbers
curl -i -X POST "http://{event-store-ip}:{ext-http-port}/projection/{projection-name}/command/enable" -H "accept:application/json" -H "Content-Length:0" -u admin:changeit
```

## By category

The `$by_category` (_http://127.0.0.1:2113/projection/$by_category_) projection links existing events from
streams to a new stream with a `$ce-` prefix (a category) by splitting a stream `id` by a configurable
separator.

```text:no-line-numbers
first
-
```

You can configure the separator, as well as where to split the stream `id`. You can edit the projection and
provide your own values if the defaults don't fit your particular scenario.

The first parameter specifies how the separator is used, and the possible values for that parameter is `first`
or `last`. The second parameter is the separator, and can be any character.

For example, if the body of the projection is `first` and `-`, for a stream id
of `account-9E763770-0A8D-456D-AF23-410ADBC88249`, the stream name the projection creates is `$ce-account`.

If the body of the projection is `last` and `-`, for a stream id of `shopping-cart-1`, the stream name the
projection creates is `$ce-shopping-cart`.

::: warning
You can change the projection setting at any time, so it can be quite dangerous. Consider all
possible event consumers of the category stream that expect it to be in the format that is already there.
Changing the setting might break all of them.
:::

The use case of this project is subscribing to all events within a category.

## By event type

The `$by_event_type` (_http://127.0.0.1:2113/projection/$by_event_type_) projection links existing events from
streams to a new stream with a stream id in the format `$et-{event-type}`.

For example, if you append an event with the `EventType` field set to `PaymentProcessed`, no matter in what
stream you appended this event, you get a link event in the `$et-PaymentProcessed` stream.

You cannot configure this projection.

## By correlation ID

The `$by_correlation_id` (_http://127.0.0.1:2113/projection/$by_correlation_id_) projection links existing
events from projections to a new stream with a stream id in the format `$bc-<correlation id>`.

The projection takes one parameter, a JSON string as a projection source:

```json:no-line-numbers
{
  "correlationIdProperty": "$myCorrelationId"
}
```

## Stream by category

The `$stream_by_category` (_http://127.0.0.1:2113/projection/$by_category_) projection links existing events
from streams to a new stream with a `$category` prefix by splitting a stream `id` by a configurable separator.

```text:no-line-numbers
first
-
```

By default, the `$stream_by_category` projection links existing events from a stream id with a name such
as `account-1` to a stream called `$category-account`. You can configure the separator as well as where to
split the stream `id`. You can edit the projection and provide your own values if the defaults don't fit your
particular scenario.

The first parameter specifies how the separator is used, and the possible values for that parameter is `first`
or `last`. The second parameter is the separator, and can be any character.

For example, if the body of the projection is `first` and `-`, for a stream id of `account-1`, the stream name
the projection creates is `$category-account`, and the `account-1` stream is linked to it. Future streams
prefixed with `account-` are likewise linked to the newly created `$category-account` stream.

If the body of the projection is last and `-`, for a stream id of `shopping-cart-1`, the stream name the
projection creates is `$category-shopping-cart`, and the `shopping-cart-1` stream is linked to it. Future
streams whose left-side split by the _last_ `-` is `shopping-cart`, are likewise linked to the newly
created `$category-shopping-cart` stream.

The use case of this projection is subscribing to all stream instances of a category.

## Streams projection

The `$streams` (_http://127.0.0.1:2113/projection/$streams_) projection links existing events from streams to
a stream named `$streams`

You cannot configure this projection.
