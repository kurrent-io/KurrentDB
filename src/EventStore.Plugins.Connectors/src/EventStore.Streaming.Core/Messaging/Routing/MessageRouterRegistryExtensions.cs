namespace EventStore.Streaming.Routing;

public static class MessageRouterRegistryExtensions {

    #region . ContainsRoute .

    /// <summary>
    /// Determines if the router can broadcast the message to any handler.
    /// </summary>
    public static bool ContainsRoute(this MessageRouterRegistry registry) =>
        registry.ContainsRoute(Route.Any);

    public static bool ContainsRoute<TMessage>(this MessageRouterRegistry registry) =>
        registry.ContainsRoute(Route.For<TMessage>(), Route.Any);

    public static bool ContainsRoute(this MessageRouterRegistry registry, IMessageContext context) =>
        registry.ContainsRoute(Route.ForType((Type)context.Message.GetType()), Route.Any);

    #endregion . ContainsRoute .

    #region . RegisterHandler .

    public static void RegisterHandler(this MessageRouterRegistry registry, IMessageHandler handler) =>
        registry.RegisterHandler(Route.Any, handler);

    public static void RegisterHandler<TContext>(this MessageRouterRegistry registry, HandleMessageAsync<TContext> handler) where TContext : IMessageContext =>
        registry.RegisterHandler(MessageHandler<TContext>.Proxy.Create(handler));

    public static void RegisterHandler<T, TContext>(this MessageRouterRegistry registry, HandleMessageAsync<T, TContext> handler) where TContext : IMessageContext =>
        registry.RegisterHandler(MessageHandler<T, TContext>.Proxy.Create(handler));

    public static void RegisterHandler<TContext>(this MessageRouterRegistry registry, Route route, HandleMessageAsync<TContext> handler)
        where TContext : IMessageContext =>
        registry.RegisterHandler(route, MessageHandler<TContext>.Proxy.Create(handler));

    public static void RegisterHandler<T, TContext>(this MessageRouterRegistry registry, Route route, HandleMessageAsync<T, TContext> handler)
        where TContext : IMessageContext =>
        registry.RegisterHandler(route, MessageHandler<T, TContext>.Proxy.Create(handler));

    public static void RegisterHandler<TContext>(this MessageRouterRegistry registry, HandleMessage<TContext> handler) where TContext : IMessageContext =>
        registry.RegisterHandler(SyncMessageHandler<TContext>.Proxy.Create(handler));

    public static void RegisterHandler<T, TContext>(this MessageRouterRegistry registry, HandleMessage<T, TContext> handler) where TContext : IMessageContext =>
        registry.RegisterHandler(SyncMessageHandler<T, TContext>.Proxy.Create(handler));

    public static void RegisterHandler<TContext>(this MessageRouterRegistry registry, Route route, HandleMessage<TContext> handler) where TContext : IMessageContext =>
        registry.RegisterHandler(route, SyncMessageHandler<TContext>.Proxy.Create(handler));

    public static void RegisterHandler<T, TContext>(this MessageRouterRegistry registry, Route route, HandleMessage<T, TContext> handler)
        where TContext : IMessageContext =>
        registry.RegisterHandler(route, SyncMessageHandler<T, TContext>.Proxy.Create(handler));

    #endregion . RegisterHandler .
}