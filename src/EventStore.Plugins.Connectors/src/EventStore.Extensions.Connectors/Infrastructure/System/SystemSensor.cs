using DotNext.Threading.Tasks;

namespace EventStore.Connectors.System;

[PublicAPI]
public class SystemSensor<T>(bool runContinuationsAsynchronously = true) {
    ValueTaskCompletionSource<T> CompletionSource { get; } = new(runContinuationsAsynchronously);

    public async ValueTask Signal(Func<ValueTask<T>> getData, CancellationToken cancellationToken) {
        if (CompletionSource.IsCompleted)
            throw new InvalidOperationException("The sensor has already been signaled. Reset the sensor by waiting for another signal.");

        if (cancellationToken.IsCancellationRequested) {
            CompletionSource.TrySetCanceled(cancellationToken);
            return;
        }

        try {
            var data = await getData();
            CompletionSource.TrySetResult(data);
        }
        catch (Exception ex) {
            CompletionSource.TrySetException(ex);
        }
    }

    public ValueTask Signal(T data, CancellationToken cancellationToken) =>
        Signal(() => ValueTask.FromResult(data), cancellationToken);

    public async ValueTask<T> WaitForSignal(TimeSpan timeout, CancellationToken cancellationToken = default) {
        CompletionSource.Reset();
        return await CompletionSource.CreateTask(timeout, cancellationToken);
    }

    public ValueTask<T> WaitForSignal(CancellationToken cancellationToken = default) =>
        WaitForSignal(Timeout.InfiniteTimeSpan, cancellationToken);
}