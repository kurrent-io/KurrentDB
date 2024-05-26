using EventStore.Streaming.Routing.Broadcasting;

namespace EventStore.Streaming.Routing;

[PublicAPI]
public class MessageModule<TContext>(MessageBroadcasterStrategy broadcasterStrategy = MessageBroadcasterStrategy.Async) where TContext : IMessageContext {
	MessageRouter Router { get; } = new(broadcasterStrategy);
	
	protected virtual void Handle(HandleMessageAsync<TContext> handler) =>
		Router.RegisterHandler(handler);
	
	protected virtual void Handle<T>(HandleMessageAsync<T, TContext> handler) =>
		Router.RegisterHandler(handler);
	
	protected virtual void Handle(Route route, HandleMessageAsync<TContext> handler) =>
		Router.RegisterHandler(route, handler);
	
	protected virtual void Handle<T>(Route route, HandleMessageAsync<T, TContext> handler) =>
		Router.RegisterHandler(route, handler);
	
	protected virtual void Handle(HandleMessage<TContext> handler) =>
		Router.RegisterHandler(handler);
	
	protected virtual void Handle<T>(HandleMessage<T, TContext> handler) =>
		Router.RegisterHandler(handler);
	
	protected virtual void Handle(Route route, HandleMessage<TContext> handler) =>
		Router.RegisterHandler(route, handler);
	
	protected virtual void Handle<T>(Route route, HandleMessage<T, TContext> handler) =>
		Router.RegisterHandler(route, handler);

	public Task Execute(TContext context) =>
		Router.Broadcast(Route.ForType((Type)context.Message.GetType()), context);
	
	public bool CanExecute(Route route) =>
		Router.CanBroadcast(route);
	
	public bool CanExecute(TContext context) =>
		Router.CanBroadcast(context);
	
	public bool CanExecute<T>() =>
		Router.CanBroadcast<T>();

	/// <summary>
	/// Combines the handlers from the current router with the provided router.
	/// </summary>
	public MessageRouter RegisterHandlersOn(MessageRouter router) =>
		router.Combine(Router);
	
	public sealed class Proxy : MessageModule<TContext> {
		public static MessageModule<TContext> For(HandleMessageAsync<TContext> handler) =>
			new Proxy().With(x => x.Handle(handler));

		public static MessageModule<TContext> For<T>(HandleMessageAsync<T, TContext> handler) =>
			new Proxy().With(x => x.Handle(handler));

		public static MessageModule<TContext> For(HandleMessage<TContext> handler) =>
			new Proxy().With(x => x.Handle(handler));

		public static MessageModule<TContext> For<T>(HandleMessage<T, TContext> handler) =>
			new Proxy().With(x => x.Handle(handler));

		public static MessageModule<TContext> For(MessageHandler<TContext> handler) =>
			new Proxy().With(x => x.Router.RegisterHandler(handler));

		public static MessageModule<TContext> For<T>(MessageHandler<T, TContext> handler) =>
			new Proxy().With(x => x.Router.RegisterHandler(handler));
	}
}
