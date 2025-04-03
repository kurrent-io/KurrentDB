#region curl
curl -i -d "SGVsbG8gV29ybGQ=" "http://127.0.0.1:2113/streams/newstream" \
    -H "Content-Type:application/octet-stream" \
    -H "Kurrent-EventType:rawDataType" \
    -H "Kurrent-EventId:eeccf3ce-4f54-409d-8870-b35dd836cca6"
#endregion curl

#region response
HTTP/1.1 201 Created
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTo, Kurrent-ExpectedVersion
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position
Location: http://127.0.0.1:2113/streams/newstream/0
Content-Type: text/plain; charset=utf-8
Server: Kestrel
Date: Mon, 27 Jun 2016 13:15:27 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
#endregion response
