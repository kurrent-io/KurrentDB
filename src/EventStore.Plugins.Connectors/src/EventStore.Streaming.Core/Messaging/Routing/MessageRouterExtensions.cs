using EventStore.Streaming.Routing.Broadcasting;

namespace EventStore.Streaming.Routing;

public static class MessageRouterExtensions {
	#region . Broadcast .

	public static Task Broadcast(this MessageRouter router, Route route, IMessageContext context, IMessageBroadcaster messageBroadcaster) =>
		router.Broadcast([route], context, messageBroadcaster);

	public static Task Broadcast(this MessageRouter router, Route route, IMessageContext context) =>
		router.Broadcast([route], context);

	public static Task Broadcast(this MessageRouter router, IMessageContext context, IMessageBroadcaster messageBroadcaster) =>
		router.Broadcast([Route.ForType((Type)context.Message.GetType()), Route.Any], context, messageBroadcaster);

	public static Task Broadcast(this MessageRouter router, IMessageContext context, MessageBroadcasterStrategy strategy) =>
		router.Broadcast(context, MessageBroadcaster.Get(strategy));

	public static Task Broadcast(this MessageRouter router, Route[] routes, IMessageContext context, MessageBroadcasterStrategy strategy) =>
		router.Broadcast(routes, context, MessageBroadcaster.Get(strategy));

	public static Task Broadcast(this MessageRouter router, Route route, IMessageContext context, MessageBroadcasterStrategy strategy) =>
		router.Broadcast(route, context, MessageBroadcaster.Get(strategy));

	#endregion . Broadcast .

	#region . CanBroadcast .

	/// <summary>
	/// Determines if the router can broadcast the message to any handler.
	/// </summary>
	public static bool CanBroadcast(this MessageRouter router) =>
		router.CanBroadcast(Route.Any);

	public static bool CanBroadcast<TMessage>(this MessageRouter router) =>
		router.CanBroadcast(Route.For<TMessage>(), Route.Any);

	public static bool CanBroadcast(this MessageRouter router, IMessageContext context) =>
		router.CanBroadcast(Route.ForType((Type)context.Message.GetType()), Route.Any);

	#endregion . CanBroadcast .

	#region . RegisterHandler .

	public static void RegisterHandler(this MessageRouter router, IMessageHandler handler) =>
		router.RegisterHandler(Route.Any, handler);

	public static void RegisterHandler<TContext>(this MessageRouter router, HandleMessageAsync<TContext> handler) where TContext : IMessageContext =>
		router.RegisterHandler(MessageHandler<TContext>.Proxy.Create(handler));

	public static void RegisterHandler<T, TContext>(this MessageRouter router, HandleMessageAsync<T, TContext> handler) where TContext : IMessageContext =>
		router.RegisterHandler(MessageHandler<T, TContext>.Proxy.Create(handler));

	public static void RegisterHandler<TContext>(this MessageRouter router, Route route, HandleMessageAsync<TContext> handler)
		where TContext : IMessageContext =>
		router.RegisterHandler(route, MessageHandler<TContext>.Proxy.Create(handler));

	public static void RegisterHandler<T, TContext>(this MessageRouter router, Route route, HandleMessageAsync<T, TContext> handler)
		where TContext : IMessageContext =>
		router.RegisterHandler(route, MessageHandler<T, TContext>.Proxy.Create(handler));

	public static void RegisterHandler<TContext>(this MessageRouter router, HandleMessage<TContext> handler) where TContext : IMessageContext =>
		router.RegisterHandler(SyncMessageHandler<TContext>.Proxy.Create(handler));

	public static void RegisterHandler<T, TContext>(this MessageRouter router, HandleMessage<T, TContext> handler) where TContext : IMessageContext =>
		router.RegisterHandler(SyncMessageHandler<T, TContext>.Proxy.Create(handler));

	public static void RegisterHandler<TContext>(this MessageRouter router, Route route, HandleMessage<TContext> handler) where TContext : IMessageContext =>
		router.RegisterHandler(route, SyncMessageHandler<TContext>.Proxy.Create(handler));

	public static void RegisterHandler<T, TContext>(this MessageRouter router, Route route, HandleMessage<T, TContext> handler)
		where TContext : IMessageContext =>
		router.RegisterHandler(route, SyncMessageHandler<T, TContext>.Proxy.Create(handler));

	#endregion . RegisterHandler .
}