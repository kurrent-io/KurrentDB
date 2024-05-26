using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Interceptors;

public readonly record struct InterceptorContext(dynamic Message, ILogger Logger, CancellationToken CancellationToken) : IMessageContext {
    MessageContextProperties IMessageContext.Properties { get; } = new();
}