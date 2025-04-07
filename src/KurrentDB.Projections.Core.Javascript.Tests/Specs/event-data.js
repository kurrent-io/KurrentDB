fromAll().when(
{
    $init: function() {
        return { events: [] };
    },
    "baz" : function(state, event){
        state.events.push({
			streamId: event.streamId,
			sequenceNumber: event.sequenceNumber,
			eventId: event.eventId,
			eventType: event.eventType,
			data: event.data,
			metadata: event.metadata
		})
    }
})
