#region curl
curl -i -X GET "http://127.0.0.1:2113/stats/proc/tcp" -u "admin:changeit"
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Cache-Control: max-age=1, public
Vary: Accept
Content-Type: application/json; charset=utf-8
Server: Kestrel
Date: Thu, 06 Dec 2018 10:03:55 GMT
Content-Length: 280
Keep-Alive: timeout=15,max=100

{
  "connections": 0,
  "receivingSpeed": 0.0,
  "sendingSpeed": 0.0,
  "inSend": 0,
  "measureTime": "00:00:05.0223780",
  "pendingReceived": 0,
  "pendingSend": 0,
  "receivedBytesSinceLastRun": 0,
  "receivedBytesTotal": 0,
  "sentBytesSinceLastRun": 0,
  "sentBytesTotal": 0
}
#endregion response
