using System.Collections;
using System.Diagnostics;

namespace EventStore.Connectors.Diagnostics;

/// <summary>
/// Generic listener that also collects the last N events and can be used to subscribe to a single source.
/// </summary>
class GenericDiagnosticsListener : IDisposable, IEnumerable<KeyValuePair<string, object?>> {
    static readonly object Locker = new();

    public GenericDiagnosticsListener(string source, int capacity = 10, Action<KeyValuePair<string, object?>>? onEvent = null) {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));

        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        Source   = source;
        Capacity = capacity;
        Queue    = new(capacity);

        var observer = new GenericObserver<KeyValuePair<string, object?>>(data => {
            if (capacity > 0)
                Queue.Enqueue(data);

            try {
                onEvent?.Invoke(data);
            }
            catch {
                // stay on target
            }
        });

        ListenerSubscription = DiagnosticListener.AllListeners
            .Subscribe(new GenericObserver<DiagnosticListener>(OnNewListener));

        return;

        void OnNewListener(DiagnosticListener listener) {
            if (listener.Name != source) return;

            lock (Locker) {
                NetworkSubscription?.Dispose();
                NetworkSubscription = listener.Subscribe(observer);
            }
        }
    }

    FixedSizedQueue<KeyValuePair<string, object?>> Queue                { get; }
    IDisposable?                                   ListenerSubscription { get; }
    IDisposable?                                   NetworkSubscription  { get; set; }

    public string Source   { get; }
    public int    Capacity { get; }

    public IReadOnlyList<KeyValuePair<string, object?>> CollectedEvents => Queue.ToList();

    public bool HasCollectedEvents => Queue.Count != 0;

    public void ClearCollectedEvents() => Queue.Clear();

    public void Dispose() {
        NetworkSubscription?.Dispose();
        ListenerSubscription?.Dispose();
        ClearCollectedEvents();
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => Queue.ToList().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    class GenericObserver<T>(Action<T>? onNext, Action? onCompleted = null) : IObserver<T> {
        public void OnNext(T value) => _onNext(value);
        public void OnCompleted()   => _onCompleted();

        public void OnError(Exception error) { }

        readonly Action<T> _onNext      = onNext      ?? (_ => { });
        readonly Action    _onCompleted = onCompleted ?? (() => { });
    }

    class FixedSizedQueue<T>(int maxSize) : Queue<T> {
        readonly object _locker = new();

        public new void Enqueue(T item) {
            lock (_locker) {
                base.Enqueue(item);
                if (Count > maxSize)
                    Dequeue(); // Throw away
            }
        }

        public new void Clear() {
            lock (_locker) {
                base.Clear();
            }
        }
    }

    public static GenericDiagnosticsListener Start(string source, int capacity = 10, Action<KeyValuePair<string, object?>>? onEvent = null) =>
        new(source, capacity, onEvent);

    public static GenericDiagnosticsListener Start(string source, Action<KeyValuePair<string, object?>>? onEvent = null) =>
        new(source, 10, onEvent);
}