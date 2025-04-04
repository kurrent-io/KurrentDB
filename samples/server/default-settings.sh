curl -i "http://localhost:2113/streams/%24settings" \
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
