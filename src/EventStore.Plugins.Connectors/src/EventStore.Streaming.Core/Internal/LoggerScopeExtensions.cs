namespace EventStore.Streaming;

static class LoggerScopeExtensions {
    public static IDisposable? BeginPropertyScope(this ILogger logger, params (string Key, object Value)[] properties) =>
        logger.BeginScope(properties.ToDictionary(p => p.Key, p => p.Value));
	
    public static IDisposable? BeginKeyedScope(this ILogger logger, string key, params (string Key, object Value)[] properties) =>
        BeginPropertyScope(logger, properties.Prepend(($"{key}.Scope", Guid.NewGuid())).ToArray());
}