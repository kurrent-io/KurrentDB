namespace McMaster.NETCore.Plugins.Internal;

class Debouncer : IDisposable {
    readonly CancellationTokenSource _cts = new();
    readonly TimeSpan                _waitTime;
    int                              _counter;

    public Debouncer(TimeSpan waitTime) => _waitTime = waitTime;

    public void Dispose() {
        _cts.Cancel();
    }

    public void Execute(Action action) {
        var current = Interlocked.Increment(ref _counter);

        Task.Delay(_waitTime).ContinueWith(
            task => {
                if (current == _counter && !_cts.IsCancellationRequested)
                    action();

                task.Dispose();
            },
            _cts.Token
        );
    }
}
