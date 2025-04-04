#region curl
curl -i http://127.0.0.1:2113/streams/newstream/2/forward/20 \
    -H "Accept: application/json"
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "1;1391431453"
Content-Type: application/json; charset=utf-8
Server: Kestrel
Date: Mon, 27 Aug 2018 09:53:14 GMT
Content-Length: 786
Keep-Alive: timeout=15,max=100

{
  "title": "Event stream 'newstream'",
  "id": "http://127.0.0.1:2113/streams/newstream",
  "updated": "0001-01-01T00:00:00Z",
  "streamId": "newstream",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": true,
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
      "uri": "http://127.0.0.1:2113/streams/newstream/0/forward/20",
      "relation": "last"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/newstream/1/backward/20",
      "relation": "next"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/newstream/metadata",
      "relation": "metadata"
    }
  ],
  "entries": []
}
#endregion response
