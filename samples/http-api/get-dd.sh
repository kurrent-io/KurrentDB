#region curl
curl -i http://localhost:2113/streams/newstream \
    -H "accept:application/vnd.kurrent.streamdesc+json"
#endregion curl

#region response
HTTP/1.1 200 Description Document
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Content-Type: application/vnd.kurrent.streamdesc+json; charset=utf-8
Server: Kestrel
Date: Thu, 23 Aug 2018 12:37:18 GMT
Content-Length: 517
Keep-Alive: timeout=15,max=100

{
  "title": "Description document for 'newstream'",
  "description": "The description document will be presented when no accept header is present or it was requested",
  "_links": {
    "self": {
      "href": "/streams/newstream",
      "supportedContentTypes": [
        "application/vnd.kurrent.streamdesc+json"
      ]
    },
    "stream": {
      "href": "/streams/newstream",
      "supportedContentTypes": [
        "application/atom+xml",
        "application/vnd.kurrent.atom+json"
      ]
    }
  }
}
#endregion response
