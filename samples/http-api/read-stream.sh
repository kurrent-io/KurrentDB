#region curl
curl -i -H "Accept:application/vnd.kurrent.atom+json" "http://127.0.0.1:2113/streams/newstream" -u "admin:changeit"
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "0;-2060438500"
Content-Type: application/vnd.kurrent.atom+json; charset=utf-8
Server: Kestrel
Date: Fri, 15 Dec 2017 12:23:23 GMT
Content-Length: 1262
Keep-Alive: timeout=15,max=100

{
  "title": "Event stream 'newstream'",
  "id": "http://127.0.0.1:2113/streams/newstream",
  "updated": "2017-12-15T12:19:32.021776Z",
  "streamId": "newstream",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": true,
  "selfUrl": "http://127.0.0.1:2113/streams/newstream",
  "eTag": "0;-2060438500",
  "links": [
    {
      "uri": "http://127.0.0.1:2113/streams/newstream",
      "relation": "self"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/newstream/head/backward/20",
      "relation": "first"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/newstream/1/forward/20",
      "relation": "previous"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/newstream/metadata",
      "relation": "metadata"
    }
  ],
  "entries": [
    {
      "title": "0@newstream",
      "id": "http://127.0.0.1:2113/streams/newstream/0",
      "updated": "2017-12-15T12:19:32.021776Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/newstream/0",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/newstream/0",
          "relation": "alternate"
        }
      ]
    }
  ]
}
#endregion response
