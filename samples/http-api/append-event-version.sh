#region curl
curl -i -d @event-version.json "http://127.0.0.1:2113/streams/newstream" \
    -H "Content-Type:application/vnd.kurrent.events+json" \
    -H "Kurrent-ExpectedVersion: 0"
#endregion curl

#region response
HTTP/1.1 201 Created
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Location: http://127.0.0.1:2113/streams/newstream/2
Content-Type: text/plain; charset=utf-8
Server: Kestrel
Date: Tue, 14 Aug 2018 10:02:08 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
#endregion response
