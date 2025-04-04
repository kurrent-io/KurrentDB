#region curl
curl -i http://127.0.0.1:2113/streams/%24all \
    -H "Accept:application/vnd.kurrent.atom+json" -u admin:changeit
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: POST, DELETE, GET, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTo, Kurrent-ExpectedVersion
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "25159393;248368668"
Content-Type: application/vnd.kurrent.atom+json; charset=utf-8
Server: Kestrel
Date: Fri, 13 Mar 2015 16:19:09 GMT
Content-Length: 12157
Keep-Alive: timeout=15,max=100

{
  "title": "All events",
  "id": "<http://127.0.0.1:2113/streams/%24all">,
  "updated": "2015-03-13T16:19:06.548415Z",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": false,
  "links": [
    {
      "uri": "http://127.0.0.1:2113/streams/%24all",
      "relation": "self"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24all/head/backward/20",
      "relation": "first"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24all/00000000000000000000000000000000/forward/20",
      "relation": "last"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24all/00000000017BC0D000000000017BC0D0/backward/20",
      "relation": "next"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24all/0000000001801EBF0000000001801EBF/forward/20",
      "relation": "previous"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/%24all/metadata",
      "relation": "metadata"
    }
  ],
  "entries": []
}
#endregion response

# TODO: Make complete
