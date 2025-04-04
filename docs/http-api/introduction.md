---
order: 1
---

# Introduction 

## Overview

KurrentDB provides a native interface of AtomPub over HTTP. AtomPub is a RESTful protocol that can reuse many existing components, for example reverse proxies and a client's native HTTP caching. Since events stored in KurrentDB are immutable, cache expiration can be infinite. KurrentDB leverages content type negotiation and you can access appropriately serialised events as JSON or according to the request headers.

### Compatibility with AtomPub

KurrentDB is compatible with the [1.0 version of the Atom Protocol](https://datatracker.ietf.org/doc/html/rfc4287). KurrentDB adds extensions to the protocol, such as headers for control and custom `rel` links.

::: warning
KurrentDB has the AtomPub protocol disabled by default. We do not advise creating new applications using AtomPub as we plan to deprecate it. Please use the gRPC protocol available in v20. It provides more reliable real-time event streaming with wide range of platforms and language supported.
:::

#### Existing implementations

Many environments have already implemented the AtomPub protocol, which simplifies the process.

| Library   | Description                                        |
|-----------|----------------------------------------------------|
| NET (BCL) | `System.ServiceModel.SyndicationServices`          |
| JVM       | <http://java-source.net/open-source/rss-rdf-tools> |
| PHP       | <http://simplepie.org/>                            |
| Ruby      | <https://github.com/cardmagic/simple-rss>          |
| Clojure   | <https://github.com/scsibug/feedparser-clj>        |
| Python    | <http://code.google.com/p/feedparser/>             |
| node.js   | <https://github.com/danmactough/node-feedparser>   |

::: warning
These are not officially supported by KurrentDB.
:::

#### Content types

The preferred way of determining which content type responses KurrentDB serves is to set the `Accept` header on the request. As some clients do not deal well with HTTP headers when caching, appending a format parameter to the URL is also supported, for example, `?format=xml`.

The accepted content types for POST requests are:

- `application/xml`
- `application/vnd.eventstore.events+xml`
- `application/json`
- `application/vnd.kurrent.events+json`
- `application/vnd.eventstore.events+json`
- `text/xml`

The accepted content types for GET requests are:

- `application/xml`
- `application/atom+xml`
- `application/json`
- `application/vnd.kurrent.atom+json`
- `application/vnd.eventstore.atom+json`
- `text/xml`
- `text/html`
- `application/vnd.kurrent.streamdesc+json`
- `application/vnd.eventstore.streamdesc+json`

## Appending Events

You append to a stream over HTTP using a `POST` request to the resource of the stream. If the stream does not exist then the stream is implicitly created.

### KurrentDB media types

KurrentDB supports a custom media type for posting events, `application/vnd.kurrent.events+json`. This format allows for extra functionality that posting events as `application/json` does not. For example it allows you to post multiple events in a single batch.

<!-- TODO: And more? Why not use it? And why are these examples not using it? -->

The format represents data with the following JSON schema (`eventId` must be a UUID).

```json
[
    {
      "eventId"    : "string",
      "eventType"  : "string",
      "data"       : "object",
      "metadata"   : "object"
    }
]
```

### Appending a single event to a new stream

If you issue a `POST` request with data to a stream and the correct content type set it appends the event to the stream, and generates a `201` response from the server, giving you the location of the event. Using the following event, which [you can also download as a file](https://raw.githubusercontent.com/EventStore/EventStore/c948d32302414b456b42a73e0ce212f264ccb30a/samples/http-api/event.json):

@[code](@httpapi/event.json)

`POST` the following request to create a stream and add an event to it:

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-to-new-stream.sh)
@tab Response
@[code{response}](@httpapi/append-event-to-new-stream.sh)
:::

Some clients may not be able to generate a unique identifier (or may not want to) for the event ID. You need this ID for idempotence purposes and KurrentDB can generate it for you.

If you leave off the `Kurrent-EventId` header you see different behavior:

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-no-id.sh)
@tab Response
@[code{response}](@httpapi/append-event-no-id.sh)
:::

In this case KurrentDB has responded with a `307 Temporary Redirect`. The location points to another URI that you can post the event to. This new URI is idempotent for posting, even without an event ID.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-follow.sh)
@tab Response
@[code{response}](@httpapi/append-event-follow.sh)
:::

It's generally recommended to include an event ID if possible as it results in fewer round trips between the client and the server.

When posting to either the stream or to the returned redirect, clients must include the `EventType` header. If you forget to include the header you receive an error.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-no-type.sh)
@tab Response
@[code{response}](@httpapi/append-event-no-type.sh)
:::

### Batch append operation

You can append more than one event in a single post by placing multiple events inside the array representing the events, including metadata.

For example, the below has two events:

@[code](@httpapi/multiple-events.json)

When you append multiple events in a single post, KurrentDB treats them as one transaction, it appends all events together or fails.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-multiple-events.sh)
@tab Response
@[code{response}](@httpapi/append-multiple-events.sh)
:::

#### Appending events

To append events, issue a `POST` request to the same resource with a new `eventId`:

@[code](@httpapi/event-append.json)

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event.sh)
@tab Response
@[code{response}](@httpapi/append-event.sh)
:::

### Data-only events

Use the `application/octet-stream` content type to support data-only binary events. When creating these events, you need to provide the `Kurrent-EventType` and `Kurrent-EventId` headers and cannot have metadata associated with the event. In the example below `SGVsbG8gV29ybGQ=` is the data you `POST` to the stream:

::: tabs
@tab Request
@[code{curl}](@httpapi/append-data-event.sh)
@tab Response
@[code{response}](@httpapi/append-data-event.sh)
:::

### Expected version header

The expected version header represents the version of the stream you expect.

For example if you append to a stream at version 1, then you expect it to be at version 1 next time you append. This can allow for optimistic locking when multiple applications are reading/appending to streams.

If your expected version is not the current version you receive an HTTP status code of 400.

::: warning
See the idempotence section below, if you post the same event twice it is idempotent and won't return a version error.
:::

First append an event to a stream, setting a version:

@[code](@httpapi/event-version.json)

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-version.sh)
@tab Response
@[code{response}](@httpapi/append-event-version.sh)
:::

If you now append to the stream with the incorrect version, you receive an HTTP status code 400 error.

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event-wrong-version.sh)
@tab Response
@[code{response}](@httpapi/append-event-wrong-version.sh)
:::

There are special values you can use in the expected version header:

- `-2` states that this append operation should never conflict and should **always** succeed.
- `-1` states that the stream should not exist at the time of the appending (this append operation creates it).
- `0` states that the stream should exist but should be empty.

### Idempotence

Appends to streams are idempotent based upon the `EventId` assigned in your post. If you were to re-run the last command it returns the same value again.

This is important behaviour as it's how you implement error handling. If you receive a timeout, broken connection, no answer, etc from your HTTP `POST` then it's your responsibility to retry the post. You must also keep the same UUID that you assigned to the event in the first `POST`.

If you are using the expected version parameter with your post, then KurrentDB is 100% idempotent. If you use `-2` as your expected version value, KurrentDB does its best to keep events idempotent but cannot assure that everything is fully idempotent and you end up in 'at-least-once' messaging.

## Reading streams and events

### Reading a stream

KurrentDB exposes streams as a resource located at `http(s)://{yourdomain.com}:{port}/streams/{stream}`. If you issue a simple `GET` request to this resource, you receive a standard AtomFeed document as a response.

::: tabs
@tab Request
@[code{curl}](@httpapi/read-stream.sh)
@tab Response
@[code{response}](@httpapi/read-stream.sh)
:::

### Reading an event from a stream

The feed has one item in it, and if there are more than one, then items are sorted from newest to oldest.

For each entry, there are a series of links to the actual events, [we cover embedding data into a stream later](#embedding-data-into-streams-in-json-format). To `GET` an event, follow the `alternate` link and set your `Accept` headers to the mime type you would like the event in.

The accepted content types for `GET` requests are:

- `application/xml`
- `application/atom+xml`
- `application/json`
- `application/vnd.kurrent.atom+json`
- `application/vnd.eventstore.atom+json`
- `text/xml`
- `text/html`

The non-atom version of the event has fewer details about the event.

::: tabs
@tab Request
@[code](@httpapi/read-event.sh)
@tab Response
@[code](@httpapi/read-event.json)
:::

### Feed paging

The next step in understanding how to read a stream is the `first`/`last`/`previous`/`next` links within a stream. KurrentDB supplies these links, so you can read through a stream, and they follow the pattern defined in [RFC 5005](https://datatracker.ietf.org/doc/html/rfc5005).

In the example above the server returned the following `links` as part of its result:

::: tabs
@tab Request
@[code{curl}](@httpapi/read-stream.sh)
@tab Response
@[code{response}](@httpapi/read-stream.sh)
:::

This shows that there is not a `next` URL as all the information is in this request and that the URL requested is the first link. When dealing with these URLs, there are two ways of reading the data in the stream.

- You `GET` the `last` link and move backwards following `previous` links, or
- You `GET` the `first` link and follow the `next` links, and the final item will not have a `next` link.

If you want to follow a live stream, then you keep following the `previous` links. When you reach the end of a stream, you receive an empty document with no entries or `previous` link. You then continue polling this URI (in the future a document will appear). You can see this by trying the `previous` link from the above feed.

::: tabs
@tab Request
@[code{curl}](@httpapi/read-stream-forwards.sh)
@tab Response
@[code{response}](@httpapi/read-stream-forwards.sh)
:::

When parsing an atom subscription, the IDs of events always stay the same. This is important for figuring out if you are referring to the same event.

### Paging through events

Let's now try an example with more than a single page. First create the multiple events:

::: tabs
@tab Request
@[code{curl}](@httpapi/append-paging-events.sh)
@tab Response
@[code{response}](@httpapi/append-paging-events.sh)
:::

If you request the stream of events, you see a series of links above the events:

::: tabs
@tab Request
@[code{curl}](@httpapi/request-paging-events.sh)
@tab Response
@[code{response}](@httpapi/request-paging-events.sh)
:::

Using the links in the stream of events, you can traverse through all the events in the stream by going to the `last` URL and following `previous` links, or by following `next` links from the `first` link.

For example, if you request the `last` link from above:

::: tabs
@tab Request
@[code{curl}](@httpapi/request-last-link.sh)
@tab Response
@[code{response}](@httpapi/request-last-link.sh)
:::

You then follow `previous` links until you are back to the head of the stream, where you can continue reading events in real time by polling the `previous` link.

::: tip
All links except the head link are fully cacheable as you can see in the HTTP header `Cache-Control: max-age=31536000, public`. This is important when discussing intermediaries and performance as you commonly replay a stream from storage. You should **never** bookmark links aside from the head of the stream resource, and always follow links. We may in the future change how internal links work, and bookmarking links other than the head may break.
:::

### Reading all events

`$all` is a special paged stream for all events. You can use the same paged form of reading described above to read all events for a node by pointing the stream at _/streams/\$all_. As it's a stream like any other, you can perform all operations, except posting to it.

::: tip
To access the `$all` stream, you must use admin details. Find more information on the [security](security.md) page.
:::

::: tabs
@tab Request
@[code{curl}](@httpapi/read-all-events.sh)
@tab Response
@[code{response}](@httpapi/read-all-events.sh)
:::

### Conditional GETs

The head link supports conditional `GET`s with the use of [ETag](http://en.wikipedia.org/wiki/HTTP_ETag), a well-known HTTP construct. You can include the ETAG of your last request and issue a conditional `GET` to the server. If nothing has changed, it won't return the full feed. For example the earlier response has an ETAG:

@[code{responseHeader}](@httpapi/request-paging-events.sh)

You can use this in your next request when polling the stream for changes by putting it in the `If-None-Match` header. This tells the server to check if the response is the one you already know and returning a '304 not modified' response. If the tags have changed, the server returns a '200 OK' response. You can use this method to optimise your application by not sending large streams if there are no changes.

::: tabs
@tab Request
@[code{curl}](@httpapi/request-etag.sh)
@tab Response
@[code{response}](@httpapi/request-etag.sh)
:::

::: tip
You create Etags using the version of the stream and the media type of the stream you are reading. You can't read an Etag from a stream in one media type and use it with another media type.
:::

### Embedding data into streams in JSON format

So far in this guide, the feeds returned have contained links that refer to the actual event data. This is normally a preferable mechanism for several reasons:

- They can be in a different media type than the feed, and you can negotiate them separately from the feed itself (for example, the feed in JSON, the event in XML). You can cache the event data separately from the feed, and you can point it to different feeds. If you use a `linkTo()` in your [projection](@server/features/projections/custom.md) this is what happens in your atom feeds.
- If you are using JSON, you can embed the events into the atom feed events. This can help cut down on the number of requests in some situations, but the messages are larger.

There are ways of embedding events and further metadata into your stream by using the `embed` parameter.

#### Rich embed mode

The `rich` embed mode returns more properties about the event (`eventtype`, `streamid`, `position`, and so on) as you can see in the following request.

::: tabs
@tab Request
@[code{curl}](@httpapi/read-stream-rich.sh)
@tab Response
@[code{response}](@httpapi/read-stream-rich.sh)
:::

#### Body embed mode

The `body` embed mode returns the JSON/XML body of the events into the feed as well, depending on the type of the feed. You can see this in the request below:

::: tabs
@tab Request
@[code{curl}](@httpapi/read-stream-body.sh)
@tab Response
@[code{response}](@httpapi/read-stream-body.sh)
:::

##### Variants of body embed mode

Two other modes are variants of `body`:

- `PrettyBody` tries to reformat the JSON to make it "pretty to read".
- `TryHarder` works harder to try to parse and reformat the JSON from an event to return it in the feed. These do not include further information and are focused on how the feed looks.

## Deleting a stream

### Soft deleting

To delete a stream over the Atom interface, issue a `DELETE` request to the resource.

::: tabs
@tab Request
@[code](@httpapi/delete-stream/delete-stream.sh)
@tab Response
@[code](@httpapi/delete-stream/delete-stream-response.http)
:::

By default, when you delete a stream, KurrentDB soft deletes it. This means you can recreate it later by setting the `$tb` metadata section in the stream. If you try to `GET` a soft deleted stream you receive a 404 response:

::: tabs
@tab Request
@[code](@httpapi/delete-stream/get-deleted-stream.sh)
@tab Response
@[code](@httpapi/delete-stream/get-deleted-stream-response.http)
:::

You can recreate the stream by appending new events to it (like creating a new stream):

::: tabs
@tab Request
@[code{curl}](@httpapi/append-event.sh)
@tab Response
@[code](@httpapi/append-event.http)
:::

The version numbers do not start at zero but at where you soft deleted the stream from

### Hard deleting

You can hard delete a stream. To issue a permanent delete use the `Kurrent-HardDelete` header.

::: warning
A hard delete is permanent and the stream is not removed during a scavenge. If you hard delete a stream, you cannot recreate the stream.
:::

Issue the `DELETE` as before but with the permanent delete header:

::: tabs
@tab Request
@[code](@httpapi/delete-stream/hard-delete-stream.sh)
@tab Response
@[code](@httpapi/delete-stream/hard-delete-stream.http)
:::

The stream is now permanently deleted, and now the response is a `410`.

::: tabs
@tab Request
@[code{curl}](@httpapi/delete-stream/get-deleted-stream.sh)
@tab Response
@[code](@httpapi/delete-stream/get-deleted-stream-response.http)
:::

If you try to recreate the stream as in the above example you also receive a `410` response.

::: tabs
@tab Request
@[code](@httpapi/delete-stream/append-event-deleted.sh)
@tab Response
@[code](@httpapi/delete-stream/append-event-deleted.http)
:::

## Description document

<!-- TODO: Combine with CC pages?  -->

With the addition of Competing Consumers, which is another way of reading streams, the need arose to expose these different methods to consumers.

The introduction of the description document has some benefits:

- Clients can rely on the keys (streams, streamSubscription) in the description document to remain unchanged across versions of KurrentDB and you can rely on it as a lookup for the particular method of reading a stream.
- Allows the restructuring of URIs underneath without breaking clients. e.g., `/streams/newstream` -> `/streams/newstream/atom`.

### Fetching the description document

There are three ways in which KurrentDB returns the description document.

- Attempting to read a stream with an unsupported media type.
- Attempting to read a stream with no accept header.
- Requesting the description document explicitly.

The client is able to request the description document by passing `application/vnd.kurrent.streamdesc+json` in the `accept` header, for example:

::: tabs
@tab Request
@[code{curl}](@httpapi/get-dd.sh)
@tab Response
@[code{response}](@httpapi/get-dd.sh)
:::

In the example above, the client requested the description document for the stream called `newstream` which has a set of links describing the supported methods and content types. The document also includes additional methods available such as the `streamSubscription`. If there are no subscriptions to the `newstream`, the `streamSubscription` key is absent.

## Optimistic concurrency and idempotence

### Idempotence

All operations on the HTTP interface are idempotent (unless the [expected version](#expected-version-header) is ignored). It is the responsibility of the client to retry operations under failure conditions, ensuring that the event IDs of the events posted are the same as the first attempt.

Provided the client maintains this KurrentDB will treat all operations as idempotent.

For example:

::: tabs
@tab Request
```bash
curl -i -d @event.txt "http://127.0.0.1:2113/streams/newstream"
```
@tab Response
```http
HTTP/1.1 201 Created
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: POST, GET, PUT, DELETE
Location: http://127.0.0.1:2113/streams/newstream444/1
Content-Type: application/json
Server: Mono-HTTPAPI/1.0
Date: Thu, 06 Sep 2012 19:49:37 GMT
Content-Length: 107
Keep-Alive: timeout=15,max=100
```
:::

Assuming you were posting to a new stream you would get the event appended once (and the stream created). The second event returns as the first but not write again.

::: tip
This allows the client rule of “if you get an unknown condition, retry” to work.
:::

For example:

::: tabs
@tab Request
```bash
curl -i "http://127.0.0.1:2113/streams/newstream444"
```
@tab Response
```http
HTTP/1.1 200 OK
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: POST, GET, PUT, DELETE
Content-Type: application/json
Server: Mono-HTTPAPI/1.0
Date: Thu, 06 Sep 2012 19:50:30 GMT
Content-Length: 2131
Keep-Alive: timeout=15,max=100

{
	"title": "Event stream 'newstream444'",
	"id": "http://127.0.0.1:2113/streams/newstream444",
	"updated": "2012-09-06T16:39:44.695643Z",
	"author": {
		"name": "Kurrent"
	},
	"links": [
		{
			"uri": "http://127.0.0.1:2113/streams/newstream444",
			"relation": "self"
		},
		{
			"uri": "http://127.0.0.1:2113/streams/newstream444",
			"relation": "first"
		}
	],
	"entries": [
		{
			"title": "newstream444 #1",
			"id": "http://127.0.0.1:2113/streams/newstream444/1",
			"updated": "2012-09-06T16:39:44.695643Z",
			"author": {
				"name": "Kurrent"
			},
			"summary": "Entry #1",
			"links": [
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/1",
					"relation": "edit"
				},
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/event/1?format=text",
					"type": "text/plain"
				},
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/event/1?format=json",
					"relation": "alternate",
					"type": "application/json"
				},
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/event/1?format=xml",
					"relation": "alternate",
					"type": "text/xml"
				}
			]
		},
		{
			"title": "newstream444 #0",
			"id": "http://127.0.0.1:2113/streams/newstream444/0",
			"updated": "2012-09-06T16:39:44.695631Z",
			"author": {
				"name": "Kurrent"
			},
			"summary": "Entry #0",
			"links": [
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/0",
					"relation": "edit"
				},
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/event/0?format=text",
					"type": "text/plain"
				},
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/event/0?format=json",
					"relation": "alternate",
					"type": "application/json"
				},
				{
					"uri": "http://127.0.0.1:2113/streams/newstream444/event/0?format=xml",
					"relation": "alternate",
					"type": "text/xml"
				}
			]
		}
	]
}
```
:::

## Stream metadata

Every stream in KurrentDB has metadata stream associated with it, prefixed by `$$`, so the metadata stream from a stream called `foo` is `$$foo`. Internally, the metadata includes information such as the ACL of the stream, the maximum count and age for the events in the stream. Client code can also add information into stream metadata for use with projections or the client API.

Stream metadata is stored internally as JSON, and you can access it over the HTTP API.

### Reading stream metadata

To read the metadata, issue a `GET` request to the attached metadata resource, which is typically of the form:

```http
https://{kurrentdb-ip-address}/streams/{stream-name}/metadata
```

You should not access metadata by constructing this URL yourself, as the right to change the resource address is reserved. Instead, you should follow the link from the stream itself, which enables your client to tolerate future changes to the addressing structure.

::: tabs
@tab Request
@[code{curl}](@httpapi/read-metadata.sh)
@tab Response
@[code{response}](@httpapi/read-metadata.sh)
:::

Once you have the URI of the metadata stream, issue a `GET` request to retrieve the metadata:

```bash
curl -i -H "Accept:application/vnd.kurrent.atom+json" https://127.0.0.1:2113/streams/%24users/metadata --user admin:changeit
```

If you have security enabled, reading metadata may require that you pass credentials, as in the examples above. If credentials are required and you do not pass them, then you receive a `401 Unauthorized` response.

::: tabs
@tab Request
@[code{curl}](@httpapi/missing-credentials.sh)
@tab Response
@[code{response}](@httpapi/missing-credentials.sh)
:::

### Writing metadata

To update the metadata for a stream, issue a `POST` request to the metadata resource.

Inside a file named _metadata.json_:

@[code](@httpapi/metadata.json)

You can also add user-specified metadata here. Some examples user-specified metadata are:

- Which adapter populates a stream.
- Which projection created a stream.
- A correlation ID to a business process.

You then post this information is then posted to the stream:

::: tabs
@tab Request
@[code{curl}](@httpapi/update-metadata.sh)
@tab Response
@[code{response}](@httpapi/update-metadata.sh)
:::

If the specified user does not have permissions to write to the stream metadata, you receive a '401 Unauthorized' response.
