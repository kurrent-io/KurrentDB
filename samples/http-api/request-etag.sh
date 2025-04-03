#region curl
curl -i http://127.0.0.1:2113/streams/alphabet \
    -H "Accept:application/vnd.kurrent.atom+json" \
    -H "If-None-Match:26;-2060438500"
#endregion curl

#region response
HTTP/1.1 304 Not Modified
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Content-Type: text/plain; charset=utf-8
Server: Kestrel
Date: Tue, 21 Aug 2018 12:07:35 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
#endregion response
