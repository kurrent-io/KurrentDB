using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Messaging;

public delegate Task NextMiddleware(IMessageContext context);

public delegate Task InvokeMiddleware(IMessageContext context, NextMiddleware next);

//public delegate Task NextMiddleware<in TContext>(TContext context) where TContext : IMessageContext;

//public delegate Task InvokeMiddleware<in TContext>(TContext context, NextMiddleware next) where TContext : IMessageContext;

public interface IMiddleware {
	Task Invoke(IMessageContext context, NextMiddleware next);
}

[PublicAPI]
public abstract class Middleware<TContext> : IMiddleware where TContext : IMessageContext {
	public abstract Task Invoke(TContext context, NextMiddleware next);

	Task IMiddleware.Invoke(IMessageContext context, NextMiddleware next) =>
		Invoke((TContext)context, next);
}

[PublicAPI]
public class MiddlewarePipeline(List<IMiddleware> middlewares) {
	public static MiddlewarePipelineBuilder Builder => new();

	public Task Execute(IMessageContext context) => ExecuteMiddleware(context, 0);

	async Task ExecuteMiddleware(IMessageContext context, int index) {
		context.CancellationToken.ThrowIfCancellationRequested();

		if (index < middlewares.Count)
			await middlewares[index].Invoke(context, nextContext => ExecuteMiddleware(nextContext, index + 1));
	}
}

[PublicAPI]
public class MiddlewarePipelineBuilder {
	List<IMiddleware> Middlewares { get; } = [];

	public MiddlewarePipelineBuilder Use(IMiddleware middleware) {
		Middlewares.Add(middleware);
		return this;
	}

	public MiddlewarePipelineBuilder Use(InvokeMiddleware middleware) =>
		Use(MiddlewareProxy.For(middleware));

	public MiddlewarePipeline Build() => new(Middlewares);
}

readonly record struct MiddlewareProxy(InvokeMiddleware InvokeMiddleware) : IMiddleware {
	public Task Invoke(IMessageContext context, NextMiddleware next) =>
		InvokeMiddleware(context, next);

	public static IMiddleware For(InvokeMiddleware middleware) =>
		new MiddlewareProxy(middleware);
}