---
order: 2
---

# Security

KurrentDB supports basic authentication for HTTP API calls, and access control lists (ACL).

## Authentication

### Creating users

KurrentDB supports basic HTTP authentication to internal users. You create these users with the HTTP API or the admin console. You need to use the credentials of the default user in the request, which has the username of `admin`, and the password of `changeit`.

When using the HTTP API, you can send the following JSON payload to the server:

@[code](@httpapi/new-user.json)

::: tabs
@tab Request
@[code](@httpapi/new-user.sh)
@tab Response
@[code](@httpapi/new-user.http)
:::

Once you have added users, you can use their details with requests.

If you were to use the wrong user or no user when a request requires one, you receive a `401 Unauthorized` response.

::: tabs
@tab Request
@[code](@httpapi/incorrect-user.sh)
@tab Response
@[code](@httpapi/incorrect-user.http)
:::

As you pass the username and password in the request we recommend you to enable SSL to encrypt the user information. [Read this guide for instructions](@server/security/README.md).

## Access control lists

Alongside authentication, KurrentDB supports per stream configuration of Access Control Lists (ACL). To configure the ACL of a stream go to its head and look for the `metadata` relationship link to fetch the metadata for the stream.

To set access control lists over HTTP you can post to the metadata stream as [with setting any other metadata](introduction.md#stream-metadata). You can also set Access Control Lists for a stream in the admin UI.

### ACL example

The ACL below gives `writer` read and write permission on the stream, while `reader` has read permission on the stream. Only users in the `$admins` group can delete the stream or read and write the metadata.

The request body placed in the file named _metadata.json_:

@[code](@httpapi/metadata.json)

Then, when you execute HTTP request as follows:

@[code{curl}](@httpapi/update-acl.sh)

You get a confirmation from the server:

@[code{response}](@httpapi/update-acl.sh)

## Default ACL

::: tip
All these examples assume you have created a user named `ouro` with password `ouroboros`.
:::

If you try to access the `$settings` stream as an unauthorized user, the server returns a 401 response.

::: tabs
@tab Request
```bash
curl -i http://127.0.0.1:2113/streams/%24settings \
    -u ouro:ouroboros
```
@tab Response
```http
HTTP/1.1 401 Unauthorized
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTo, Kurrent-ExpectedVersion
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position
WWW-Authenticate: Basic Realm="Kurrent"
Content-Type: text/plain; charset=utf-8
Server: Mono-HTTPAPI/1.0
Date: Mon, 02 Mar 2015 15:21:27 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
```
:::

If you wanted to give `ouro` access by default to system streams, POST the following JSON:

```json
{
  "$userStreamAcl": {
    "$r": "$all",
    "$w": "ouro",
    "$d": "ouro",
    "$mr": "ouro",
    "$mw": "ouro"
  },
  "$systemStreamAcl": {
    "$r": ["$admins", "ouro"],
    "$w": "$admins",
    "$d": "$admins",
    "$mr": "$admins",
    "$mw": "$admins"
  }
}
```

At which point `ouro` can read system streams by default:

::: tabs
@tab Request
```bash
curl -i http://127.0.0.1:2113/streams/%24settings \
    -u ouro:ouroboros
```
@tab Response
```http
HTTP/1.1 200 OK
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTo, Kurrent-ExpectedVersion
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "1;-1296467268"
Content-Type: application/atom+xml; charset=utf-8
Server: Mono-HTTPAPI/1.0
Date: Mon, 02 Mar 2015 15:25:17 GMT
Content-Length: 1286
Keep-Alive: timeout=15,max=100
```
:::

You can also limit ACLs on particular streams which are then merged with the default ACLs.

```json
{
  "$acl": {
    "$r": ["reader", "also-reader"]
  }
}
```

If you add the above to a stream's ACL, then it overrides the read permission on that stream to allow `reader` and `also-reader` to read streams, but not `ouro`, resulting in the effective ACL below.

```json
{
  "$acl": {
    "$r": ["reader", "also-reader"],
    "$w": "ouro",
    "$d": "ouro",
    "$mr": "ouro",
    "$mw": "ouro"
  }
}
```

::: warning
Caching is allowed on a stream if you have enabled it to be visible to `$all`. This is as a performance optimization to avoid having to set `cache=private` on all data. If people are bookmarking your URIs and they were cached by an intermediary then they may still be accessible after you change the permissions from `$all`. While clients should not be bookmarking URIs in this way, it's an important consideration.
:::
