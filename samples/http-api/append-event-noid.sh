curl -i -d @event-no-id.json "http://127.0.0.1:2113/streams/newstream" \
    -H "Content-Type:application/vnd.kurrent.events+json"
