namespace EventStore.Streaming.Routing;

public interface IMessageHandler {
	Task Handle(IMessageContext context);
}
public interface IMessageHandler<in TMessage> : IMessageHandler{
    Task Handle(TMessage message, IMessageContext context);

    new Task Handle(IMessageContext context) => Handle((TMessage)context.Message, context);
}

public delegate Task HandleMessageAsync<in TContext>(TContext context) where TContext : IMessageContext;

public delegate Task HandleMessageAsync<in TMessage, in TContext>(TMessage message, TContext context) where TContext : IMessageContext;

public delegate void HandleMessage<in TContext>(TContext context) where TContext : IMessageContext;

public delegate void HandleMessage<in TMessage, in TContext>(TMessage message, TContext context) where TContext : IMessageContext;

[PublicAPI]
public abstract class MessageHandler<TMessage, TContext> : IMessageHandler where TContext : IMessageContext {
	public abstract Task Handle(TMessage message, TContext context);

	Task IMessageHandler.Handle(IMessageContext context) => Handle((TMessage)context.Message, (TContext)context);

	internal class Proxy(HandleMessageAsync<TMessage, TContext> handler) : MessageHandler<TMessage, TContext> {
		public override Task Handle(TMessage message, TContext context) => handler(message, context);

		public static IMessageHandler Create(HandleMessageAsync<TMessage, TContext> handler) => new Proxy(handler);
	}
}

[PublicAPI]
public abstract class MessageHandler<TContext> : IMessageHandler where TContext : IMessageContext {
	public abstract Task Handle(TContext context);

	Task IMessageHandler.Handle(IMessageContext context) => Handle((TContext)context);

	internal class Proxy(HandleMessageAsync<TContext> handler) : MessageHandler<TContext> {
		public override Task Handle(TContext context) => handler(context);

		public static IMessageHandler Create(HandleMessageAsync<TContext> handler) => new Proxy(handler);
	}
}

[PublicAPI]
public abstract class SyncMessageHandler<TMessage, TContext> : IMessageHandler where TContext : IMessageContext {
	public abstract void Handle(TMessage message, TContext context);

	Task IMessageHandler.Handle(IMessageContext context) {
		Handle((TMessage)context.Message, (TContext)context);
		return Task.CompletedTask;
	}

	internal class Proxy(HandleMessage<TMessage, TContext> handler) : SyncMessageHandler<TMessage, TContext> {
		public override void Handle(TMessage message, TContext context) => handler(message, context);

		public static IMessageHandler Create(HandleMessage<TMessage, TContext> handler) => new Proxy(handler);
	}
}

[PublicAPI]
public abstract class SyncMessageHandler<TContext> : IMessageHandler where TContext : IMessageContext {
	public abstract void Handle(TContext context);

	Task IMessageHandler.Handle(IMessageContext context) {
		Handle((TContext)context);
		return Task.CompletedTask;
	}

	internal class Proxy(HandleMessage<TContext> handler) : SyncMessageHandler<TContext> {
		public override void Handle(TContext context) => handler(context);

		public static IMessageHandler Create(HandleMessage<TContext> handler) => new Proxy(handler);
	}
}