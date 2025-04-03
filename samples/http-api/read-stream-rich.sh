#region curl
curl -i -H "Accept:application/vnd.kurrent.atom+json" \
    "http://127.0.0.1:2113/streams/newstream?embed=rich"
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTo, Kurrent-ExpectedVersion
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "0;248368668"
Content-Type: application/vnd.kurrent.atom+json; charset=utf-8
Server: Kestrel
Date: Fri, 13 Mar 2015 16:30:57 GMT
Content-Length: 1570
Keep-Alive: timeout=15,max=100

{
  "title": "Event stream 'newstream'",
  "id": "<http://127.0.0.1:2113/streams/newstream">,
  "updated": "2015-03-13T12:13:42.492473Z",
  "streamId": "newstream",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": true,
  "selfUrl": "<http://127.0.0.1:2113/streams/newstream">,
  "eTag": "0;248368668",
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
      "eventId": "fbf4a1a1-b4a3-4dfe-a01f-ec52c34e16e4",
      "eventType": "event-type",
      "eventNumber": 0,
      "streamId": "newstream",
      "isJson": true,
      "isMetaData": false,
      "isLinkMetaData": false,
      "positionEventNumber": 0,
      "positionStreamId": "newstream",
      "title": "0@newstream",
      "id": "<http://127.0.0.1:2113/streams/newstream/0">,
      "updated": "2015-03-13T12:13:42.492473Z",
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
