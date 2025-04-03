curl -i -d "@event-append.json" "http://127.0.0.1:2113/streams/newstream" \
    -H "Content-Type:application/vnd.kurrent.events+json" \
    -H "Kurrent-EventType: SomeEvent"
