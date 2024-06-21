namespace EventStore.Connectors.Diagnostics;

public delegate void OnSourceEvent(string source, object data);

/// <summary>
/// Generic listener that can subscribe to multiple sources, ignores the default diagnostics model and always returns just the value and only if its not null.
/// </summary>
public class MultiSourceDiagnosticsListener : IDisposable {
	public MultiSourceDiagnosticsListener(string[] sources, int capacity = 10, OnSourceEvent? onEvent = null) {
		foreach (var source in sources)
			Listeners.TryAdd(source, new(source, capacity, data => onEvent?.Invoke(source, data)));
	}

	Dictionary<string, SingleSourceDiagnosticsListener> Listeners { get; } = new();

	public IEnumerable<object> CollectedEvents(string source) =>
		Listeners.TryGetValue(source, out var listener) ? (IEnumerable<object>)listener : [];

	public bool HasCollectedEvents(string source) =>
		Listeners.TryGetValue(source, out var listener) && listener.HasCollectedEvents;

	public void ClearCollectedEvents(string source) {
		if (Listeners.TryGetValue(source, out var listener))
			listener.ClearCollectedEvents();
	}

	public void ClearAllCollectedEvents() {
		foreach (var listener in Listeners.Values)
			listener.ClearCollectedEvents();
	}

	public void Dispose() {
		foreach (var listener in Listeners.Values)
			listener.Dispose();

		Listeners.Clear();
	}

    public static MultiSourceDiagnosticsListener Start(OnSourceEvent onEvent, params string[] sources) =>
        new(sources, 10, onEvent);

	public static MultiSourceDiagnosticsListener Start(OnSourceEvent onEvent, int capacity, params string[] sources) =>
		new(sources, capacity, onEvent);

	public static MultiSourceDiagnosticsListener Start(params string[] sources) =>
		new(sources);

	public static MultiSourceDiagnosticsListener Start(int capacity, params string[] sources) =>
		new(sources, capacity);
}