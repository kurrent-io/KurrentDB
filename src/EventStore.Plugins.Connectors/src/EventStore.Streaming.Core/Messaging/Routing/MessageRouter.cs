using EventStore.Streaming.Routing.Broadcasting;

namespace EventStore.Streaming.Routing;

[PublicAPI]
public class MessageRouter(MessageRouterRegistry registry, IMessageBroadcaster broadcaster) {
    public MessageRouter(MessageBroadcasterStrategy strategy = MessageBroadcasterStrategy.Async)
		: this(new(), MessageBroadcaster.Get(strategy)) { }
    
    public MessageRouter(MessageRouterRegistry registry, MessageBroadcasterStrategy strategy = MessageBroadcasterStrategy.Async)
        : this(registry, MessageBroadcaster.Get(strategy)) { }

    internal MessageRouterRegistry Registry { get; } = registry;

    IMessageBroadcaster Broadcaster { get; } = broadcaster;

    public IReadOnlyCollection<EndpointInfo> Endpoints =>
        Registry.Endpoints.Select(x => new EndpointInfo(x.Route, x.Handlers.Select(h => h.GetType().Name).ToArray())).ToArray();

	public void RegisterHandler(Route route, IMessageHandler handler) =>
		Registry.RegisterHandler(route, handler);

	public Task Broadcast(Route[] routes, IMessageContext context, IMessageBroadcaster messageBroadcaster) =>
		messageBroadcaster.Broadcast(context, Registry.GetHandlers(routes));

	public Task Broadcast(Route[] routes, IMessageContext context) =>
		Broadcast(routes, context, Broadcaster);

	public bool CanBroadcast(params Route[] routes) =>
		Registry.ContainsRoute(routes);

	public MessageRouter Combine(MessageRouter router) {
		Registry.Combine(router.Registry);
		return this;
	}
}