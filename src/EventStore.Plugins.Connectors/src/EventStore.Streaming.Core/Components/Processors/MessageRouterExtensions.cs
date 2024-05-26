using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Processors;

public static class MessageRouterExtensions {
    public static async Task ProcessRecord(this MessageRouter router, RecordContext context) {
        if (router.CanBroadcast(Route.Any))
            await router.Broadcast(Route.Any, context);

        if (router.CanBroadcast())
            await router.Broadcast(Route.ForType(context.Record.ValueType), context);
    }

    public static bool CanProcessRecord(this MessageRouter router, EventStoreRecord record) =>
        router.CanBroadcast(Route.ForType(record.ValueType), Route.Any);
}