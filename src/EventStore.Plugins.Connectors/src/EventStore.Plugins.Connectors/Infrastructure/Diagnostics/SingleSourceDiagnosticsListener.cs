using System.Collections;

namespace EventStore.Connectors.Diagnostics;

/// <summary>
/// Generic listener that ignores the default diagnostics model and always returns just the value and only if its not null.
/// </summary>
public class SingleSourceDiagnosticsListener : IEnumerable<object>, IDisposable {
    public SingleSourceDiagnosticsListener(string source, int capacity = 10, Action<object>? onEvent = null) {
        Listener = new(source, capacity, data => {
            if (data.Value is not null)
                onEvent?.Invoke(data.Value);
        });
    }

    GenericDiagnosticsListener Listener { get; }

    List<object> ValidEvents => Listener.CollectedEvents
        .Where(x => x.Value is not null)
        .Select(x => x.Value!)
        .ToList();

    public string Source   => Listener.Source;
    public int    Capacity => Listener.Capacity;

    public IReadOnlyList<object> CollectedEvents => ValidEvents;

    public bool HasCollectedEvents => Listener.HasCollectedEvents;

    public void ClearCollectedEvents() => Listener.ClearCollectedEvents();

    public IEnumerator<object> GetEnumerator() => ValidEvents.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose() => Listener.Dispose();

    public static SingleSourceDiagnosticsListener Start(string source, int capacity) =>
        new(source, capacity);

    public static SingleSourceDiagnosticsListener Start(string source) =>
        new(source);

    public static SingleSourceDiagnosticsListener Start(Action<object> onEvent, string source) =>
        new(source, 10, onEvent);

    public static SingleSourceDiagnosticsListener Start(Action<object> onEvent, int capacity, string source) =>
        new(source, capacity, onEvent);
}