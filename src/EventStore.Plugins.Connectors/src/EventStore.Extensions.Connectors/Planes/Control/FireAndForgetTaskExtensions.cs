namespace EventStore.Connectors.Control;

public static class FireAndForgetTaskExtensions {
    public static void FireAndForget(this Task operation, CancellationToken cancellationToken = default) => _ = Task.Run(async () => await operation, cancellationToken);
    public static void FireAndForget(this ValueTask operation, CancellationToken cancellationToken = default) => _ = Task.Run(async () => await operation, cancellationToken);
}
