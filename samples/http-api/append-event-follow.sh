#region curl
curl -i -d "@event.json" "http://127.0.0.1:2113/streams/newstream/incoming/8a00e469-3a99-4517-a0b0-8dc662ffad9b" \
    -H "Content-Type: application/json" -H "Kurrent-EventType: SomeEvent"
#endregion curl

#region response
HTTP/1.1 201 Created
Access-Control-Allow-Methods: GET, POST, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Location: http://127.0.0.1:2113/streams/newstream/0
Content-Type: text/plain; charset=utf-8
Server: Kestrel
Date: Tue, 24 Jul 2018 14:46:10 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
#endregion response
