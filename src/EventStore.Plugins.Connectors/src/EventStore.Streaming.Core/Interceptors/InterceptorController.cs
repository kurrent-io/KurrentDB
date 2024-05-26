using EventStore.Streaming.Routing;
using EventStore.Streaming.Routing.Broadcasting;

namespace EventStore.Streaming.Interceptors;

/// <summary>
///  A controller for interceptors.
///  This class is responsible for managing the lifecycle of interceptors and routing events to them.
///   
/// </summary>
[PublicAPI]
public class InterceptorController : IAsyncDisposable {
    public InterceptorController(IEnumerable<InterceptorModule> interceptors, ILogger logger) {
        Interceptors = interceptors;
        
        Router = Interceptors.Aggregate(
            new MessageRouter(MessageBroadcasterStrategy.Sequential),
            static (router, module) => module.RegisterHandlersOn(router)
        );

        Logger = logger;
    }

    IEnumerable<InterceptorModule> Interceptors { get; }
    ILogger                        Logger       { get; }
    MessageRouter                  Router       { get; }

    public async Task Intercept<T>(T evt, CancellationToken cancellationToken = default) where T : InterceptorEvent {
        var route = Route.For<T>();

        if (!Router.CanBroadcast(route))
            return;

        var context = new InterceptorContext(evt, Logger, cancellationToken);
		
        await Router.Broadcast(route, context);
    }

    public async ValueTask DisposeAsync() {
        foreach (var interceptor in Interceptors)
            try {
                await interceptor.DisposeAsync();
            }
            catch (Exception ex) {
                Logger.LogWarning(ex, "{Interceptor} failed to dispose", interceptor.Name);
            }
    }
}
