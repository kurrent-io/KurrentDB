namespace EventStore.Streaming.Persistence.State;

public interface IStateStore {
    ValueTask<T>  Set<T>(object key, T value, CancellationToken cancellationToken = default);
    ValueTask<T?> Get<T>(object key, CancellationToken cancellationToken = default);
    ValueTask     Delete<T>(object key, CancellationToken cancellationToken = default);
    ValueTask<T?> GetOrSet<T>(object key, Func<ValueTask<T?>> factory, CancellationToken cancellationToken = default);
}

public delegate IStateStore StateStoreFactory(string groupName);