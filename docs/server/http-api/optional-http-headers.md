---
order: 5
---

# Optional HTTP headers

<!-- TODO: Can Swagger replace this? And sub files -->

KurrentDB supports custom HTTP headers for requests in the form `Kurrent-*`.

The headers supported are:

| Header                                                                   | Description                                                                                        |
|--------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------|
| [Kurrent-ExpectedVersion](#expected-version)                                  | The expected version of the stream (allows optimistic concurrency)                                 |
| [Kurrent-ResolveLinkTo](#resolve-linkto)                                      | Whether to resolve `linkTos` in stream                                                             |
| [Kurrent-RequireLeader](#require-leader)                                    | Whether this operation needs to run on the leader node                                             |
| [Kurrent-TrustedAuth](@server/security/user-authentication.md#externalised-authentication) | Allows a trusted intermediary to handle authentication                                             |
| [Kurrent-LongPoll](#longpoll)                                                 | Instructs the server to do a long poll operation on a stream read                                  |
| [Kurrent-HardDelete](#harddelete)                                             | Instructs the server to hard delete the stream when deleting as opposed to the default soft delete |
| [Kurrent-EventType](#eventtype)                                               | Instructs the server the event type associated to a posted body                                    |
| [Kurrent-EventId](#eventid)                                                   | Instructs the server the event id associated to a posted body                                      |

## EventID

When you append to a stream and don't use the `application/vnd.kurrent.events+json` media type, you need to specify an event ID with the event you post. This is not required with the custom media type as it is specified within the format (there is an `EventId` on each entry in the format). KurrentDB uses `EventId` for impotency.

You can include an event ID on an event by specifying this header.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-to-new-stream.sh)
@tab Response
@[code{response}](@httpapi/append-event-to-new-stream.sh)
:::

If you don't add an `Kurrent-EventId` header on an append where the body is considered the actual event (e.g., not using `application/vnd.kurrent.events+json`) KurrentDB generates a unique identifier for you and redirects you to an idempotent URI where you can post your event. If you can create a UUID then you shouldn't use this feature, but it's useful when you cannot create a UUID.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-no-id.sh)
@tab Response
@[code{response}](@httpapi/append-event-no-id.sh)
:::

KurrentDB returned a `307 Temporary Redirect` with a location header that points to a generated URI that is idempotent for purposes of retrying the post.

## EventType

When you append to a stream and don't the `application/vnd.kurrent.events+json/` media type you must specify an event type with the event that you are posting. This isn't required with the custom media type as it's specified within the format itself.

You use the `Kurrent-EventType` header as follows.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-to-new-stream.sh)
@tab Response
@[code{response}](@httpapi/append-event-to-new-stream.sh)
:::

If you view the event in the UI or with cURL it has the `EventType` of `SomeEvent`:

<!-- TODO: Does this make sense? If I can't use the custom media type -->

::: tabs
@tab Request
@[code](@httpapi/read-event.sh)
@tab Response
@[code](@httpapi/read-event.json)
:::

## Expected Version

When you append to a stream you often want to use `Expected Version` to allow for optimistic concurrency with a stream. You commonly use this for a domain object projection.

i.e., "A append operations can succeed if I have seen everyone else's append operations."

You set `ExpectedVersion` with the syntax `Kurrent-ExpectedVersion: #`, where `#` is an integer version number. There are other special values available:

- `0`, the stream should exist but be empty when appending.
- `-1`, the stream should not exist when appending.
- `-2`, the write should not conflict with anything and should always succeed.
- `-4`, the stream or a metadata stream should exist when appending.

If the `ExpectedVersion` does not match the version of the stream, KurrentDB returns an HTTP 400 `Wrong expected EventNumber` response. This response contains the current version of the stream in an `Kurrent-CurrentVersion` header.

In the following cURL command `ExpectedVersion` is not set, and it appends or create/append to the stream.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-to-new-stream.sh)
@tab Response
@[code{response}](@httpapi/append-event-to-new-stream.sh)
:::

The stream `newstream` has one event. If you append with an expected version of `3`, you receive an error.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-wrong-version.sh)
@tab Response
@[code{response}](@httpapi/append-event-wrong-version.sh)
:::

You can see from the `Kurrent-CurrentVersion` header above that the stream is at version 0. Appending with an expected version of 0 works. The expected version is always the version of the last event known in the stream.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-version.sh)
@tab Response
@[code{response}](@httpapi/append-event-version.sh)
:::

## HardDelete

The `Kurrent-HardDelete` header controls deleting a stream. By default KurrentDB soft deletes a stream allowing you to later reuse that stream. If you set the `Kurrent-HardDelete` header KurrentDB permanently deletes the stream.

::: tabs
@tab Request
@[code](@httpapi/delete-stream/hard-delete-stream.sh)
@tab Response
@[code](@httpapi/delete-stream/hard-delete-stream.http)
:::

This changes the general behaviour from returning a `404` and the stream to be recreated (soft-delete) to the stream now return a `410 Deleted` response.

## LongPoll

You use the `Kurrent-LongPoll` header to tell KurrentDB that when on the head link of a stream and no data is available to wait a period of time to see if data becomes available.

You can use this to give lower latency for Atom clients instead of client initiated polling.

Instead of the client polling every 5 seconds to get data from the feed the client sends a request with `Kurrent-LongPoll: 15`. This instructs KurrentDB to wait for up to 15 seconds before returning with no result. The latency is therefore lowered from the poll interval to about 10ms from the time an event is appended until the time the HTTP connection is notified.

You can see the use of the `Kurrent-LongPoll` header in the following cURL command.

First go to the head of the stream.

::: tabs
@tab Request
@[code{curl}](@httpapi/read-stream.sh)
@tab Response
@[code{response}](@httpapi/read-stream.sh)
:::

Then fetch the previous `rel` link `http://127.0.0.1:2113/streams/newstream/2/forward/20` and try it. It returns an empty feed.

::: tabs
@tab Request
@[code{curl}](@httpapi/get-forward-link.sh)
@tab Response
@[code{response}](@httpapi/get-forward-link.sh)
:::

The entries section is empty (there is no further data to provide). Now try the same URI with a long poll header.

@[code](@httpapi/longpoll.sh)

If you do not insert any events into the stream while this is running it takes 10 seconds for the HTTP request to finish. If you append an event to the stream while its running you see the result for that request when you append the event.

## Require Leader

When running in a clustered environment there are times when you only want an operation to happen on the current leader node. A client can fetch information in an eventually consistent fashion by communicating with the servers. The TCP client included with the multi-node version does this.

Over HTTP the `RequireLeader` header tells the node that it is not allowed to serve a read or forward a write request. If the node is the leader everything works as normal, if it isn't it responds with a `307` temporary redirect to the leader.

Run the below on the leader:

::: tabs
@tab Request
```bash
curl -i "http://127.0.0.1:32004/streams/newstream" \
    -H "Kurrent-RequireLeader: True"
```
@tab Response
```json
HTTP/1.1 200 OK
Cache-Control: max-age=0, no-cache, must-revalidate
Content-Length: 1296
Content-Type: application/vnd.kurrent.atom+json; charset: utf-8
ETag: "0;-2060438500"
Vary: Accept
Server: Microsoft-HTTPAPI/2.0
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER
Access-Control-Allow-Origin: *
Date: Thu, 27 Jun 2013 14:48:37 GMT

{
  "title": "Event stream 'stream'",
  "id": "http://127.0.0.1:32004/streams/stream",
  "updated": "2013-06-27T14:48:15.2596358Z",
  "streamId": "stream",
  "author": {
    "name": "Kurrent"
  },
  "links": [
    {
      "uri": "http://127.0.0.1:32004/streams/stream",
      "relation": "self"
    },
    {
      "uri": "http://127.0.0.1:32004/streams/stream/head/backward/20",
      "relation": "first"
    },
    {
      "uri": "http://127.0.0.1:32004/streams/stream/0/forward/20",
      "relation": "last"
    },
    {
      "uri": "http://127.0.0.1:32004/streams/stream/1/forward/20",
      "relation": "previous"
    },
    {
      "uri": "http://127.0.0.1:32004/streams/stream/metadata",
      "relation": "metadata"
    }
  ],
  "entries": [
    {
      "title": "0@stream",
      "id": "http://127.0.0.1:32004/streams/stream/0",
      "updated": "2013-06-27T14:48:15.2596358Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "TakeSomeSpaceEvent",
      "links": [
        {
          "uri": "http://127.0.0.1:32004/streams/stream/0",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:32004/streams/stream/0",
          "relation": "alternate"
        }
      ]
    }
  ]
}
```
:::

Run the following on any other node:

::: tabs
@tab Request
```bash
curl -i "http://127.0.0.1:31004/streams/newstream" \
    -H "Kurrent-RequireLeader: True"
```
@tab Response
```http
HTTP/1.1 307 Temporary Redirect
Content-Length: 0
Content-Type: text/plain; charset: utf-8
Location: http://127.0.0.1:32004/streams/stream
Server: Microsoft-HTTPAPI/2.0
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER
Access-Control-Allow-Origin: *
Date: Thu, 27 Jun 2013 14:48:28 GMT
```
:::

## Resolve LinkTo

When using projections you can have links placed into another stream. By default KurrentDB always resolve `linkTo`s for you returning the event that points to the link. You can use the `Kurrent-ResolveLinkTos: false` HTTP header to tell KurrentDB to return you the actual link and to not resolve it.

You can see the differences in behaviour in the following cURL commands.

::: tabs
@tab Request
@[code{curl}](@httpapi/resolve-links.sh)
@tab Response
@[code{response}](@httpapi/resolve-links.sh)
:::

::: tip
The content links are pointing to the original projection stream. The linked events are resolved back to where they point. With the header set the links (or embedded content) instead point back to the actual `linkTo` events.
:::

::: tabs
@tab Request
@[code{curl}](@httpapi/resolve-links-false.sh)
@tab Response
@[code{response}](@httpapi/resolve-links-false.sh)
:::
