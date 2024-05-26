namespace EventStore.Streaming.Routing;

public record Endpoint(Route Route, IReadOnlyCollection<IMessageHandler> Handlers);

public record EndpointInfo(Route Route, IReadOnlyCollection<string> Handlers);