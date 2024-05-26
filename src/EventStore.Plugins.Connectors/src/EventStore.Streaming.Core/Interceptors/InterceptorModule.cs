using EventStore.Streaming.Routing;
using EventStore.Streaming.Routing.Broadcasting;

namespace EventStore.Streaming.Interceptors;

[PublicAPI]
public abstract class InterceptorModule : IAsyncDisposable {
	protected InterceptorModule(string? name = null) {
		Name          = name ?? GetType().Name.Replace("Module", "");
		LogProperties = [("Interceptor", Name)];
		Router        = new(MessageBroadcasterStrategy.Sequential);
	}

    public string Name { get; }

    (string Key, object Value)[] LogProperties { get; }
    MessageRouter                Router        { get; }
    
	protected void On<T>(Func<T, InterceptorContext, Task> handler) where T : InterceptorEvent =>
		Router.RegisterHandler<T, InterceptorContext>(
			async (evt, ctx) => {
				using var scope = ctx.Logger.BeginPropertyScope(LogProperties);
				try {
					await handler(evt, ctx);
				}
				catch (Exception ex) {
					ctx.Logger.LogWarning(ex, "{Interceptor} failed to intercept {EventName}", Name, typeof(T).Name);
				}
			}
		);
	
	protected void On<T>(Action<T, InterceptorContext> handler) where T : InterceptorEvent =>
		On<T>(handler.InvokeAsync);
	
	public Task Intercept(InterceptorContext context) =>
		Router.Broadcast(Route.ForType((Type)context.Message.GetType()), context);
	
	public MessageRouter RegisterHandlersOn(MessageRouter router) =>
		router.Combine(Router);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}