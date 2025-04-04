#region curl
curl -i -H "Accept:application/vnd.kurrent.atom+json" \
    http://127.0.0.1:2113/streams/%24users --user admin:changeit
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "3;-2060438500"
Content-Type: application/vnd.kurrent.atom+json; charset=utf-8
Server: Kestrel
Date: Thu, 23 Aug 2018 10:03:34 GMT
Content-Length: 2670
Keep-Alive: timeout=15,max=100

{
  "title": "Event stream '$users'",
  "id": "http://127.0.0.1:2113/streams/%24users",
  "updated": "2018-08-23T09:19:37.880827Z",
  "streamId": "$users",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": true,
  "selfUrl": "http://127.0.0.1:2113/streams/%24users",
  "eTag": "3;-2060438500",
  "links": [
    {
      "uri": "http://127.0.0.1:2113/streams/%24users",
      "relation": "self"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24users/head/backward/20",
      "relation": "first"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24users/4/forward/20",
      "relation": "previous"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24users/metadata",
      "relation": "metadata"
    }
  ],
  "entries": [
    {
      "title": "3@$users",
      "id": "http://127.0.0.1:2113/streams/%24users/3",
      "updated": "2018-08-23T09:19:37.880827Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "$User",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/3",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/3",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "2@$users",
      "id": "http://127.0.0.1:2113/streams/%24users/2",
      "updated": "2018-08-23T09:08:40.499762Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "$User",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/2",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/2",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "1@$users",
      "id": "http://127.0.0.1:2113/streams/%24users/1",
      "updated": "2018-08-23T07:55:39.833203Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "$User",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/1",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/1",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "0@$users",
      "id": "http://127.0.0.1:2113/streams/%24users/0",
      "updated": "2018-08-23T07:55:39.829589Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "$User",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/0",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/%24users/0",
          "relation": "alternate"
        }
      ]
    }
  ]
}
#endregion response
