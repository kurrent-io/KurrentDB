#region curl
curl -i "http://127.0.0.1:2113/streams/%24settings" \
    --user admin:changeit \
    -H "Content-Type: application/vnd.kurrent.events+json" \
    -d $'[{
        "eventId": "7c314750-05e1-439f-b2eb-f5b0e019be72",
        "eventType": "update-default-acl",
        "data": {
            "$userStreamAcl" : {
                "$r"  : ["$admin", "$ops", "service-a", "service-b"],
                "$w"  : ["$admin", "$ops", "service-a", "service-b"],
                "$d"  : ["$admin", "$ops"],
                "$mr" : ["$admin", "$ops"],
                "$mw" : ["$admin", "$ops"]
            },
            "$systemStreamAcl" : {
                "$r"  : "$admins",
                "$w"  : "$admins",
                "$d"  : "$admins",
                "$mr" : "$admins",
                "$mw" : "$admins"
            }
        }
    }]'
#endregion curl

HTTP/1.1 201 Created
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Location: http://127.0.0.1:2113/streams/%24settings/0
Content-Type: text/plain; charset=utf-8
Server: Kestrel
Date: Fri, 04 Oct 2019 08:47:45 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
