#region curl
curl -i -d "@event.json" "http://127.0.0.1:2113/streams/newstream" \
    -H "Content-Type:application/vnd.kurrent.events+json"
    -u "admin:changeit"
#endregion curl

#region response
HTTP/1.1 201 Created
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-PINGOTHER
Access-Control-Allow-Origin: *
Location: http://127.0.0.1:2113/streams/newstream/0
Content-Type: text/plain; charset: utf-8
Server: Kestrel
Date: Fri, 28 Jun 2013 12:17:59 GMT
Content-Length: 0
Keep-Alive: timeout=15,max=100
#endregion response
