#region curl
curl -i -H "Accept:application/vnd.kurrent.atom+json" http://127.0.0.1:2113/streams/%24users
#endregion curl

#region response
HTTP/1.1 401 Unauthorized
Access-Control-Allow-Methods: GET, POST, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
WWW-Authenticate: Basic Realm="Kurrent"
Content-Type: text/plain; charset=utf-8
Server: Kestrel
Date: Thu, 23 Aug 2018 10:26:52 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
#endregion response
