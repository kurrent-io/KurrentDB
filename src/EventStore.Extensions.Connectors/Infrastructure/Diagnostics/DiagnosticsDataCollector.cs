using System.Collections.Concurrent;

namespace EventStore.Connectors.Diagnostics;

/// <summary>
///     A delegate to handle <see cref="DiagnosticsData" /> events.
/// </summary>
public delegate void OnEventCollected(DiagnosticsData diagnosticsData);

/// <summary>
///     Component to collect diagnostics data. More specifically <see cref="DiagnosticsData" /> events.
/// </summary>
[PublicAPI]
public class DiagnosticsDataCollector : IDisposable {
	public DiagnosticsDataCollector(string[] sources, int capacity = 10, OnEventCollected? onEventCollected = null) {
		Listener = new(sources, 0, (source, data) => {
			if (data is not DiagnosticsData pluginData) return;

			CollectedEventsByPlugin.AddOrUpdate(
				source,
				static (_, state) => [state.PluginData],
				static (_, collected, state) => {
					switch (state.PluginData.CollectionMode) {
						case DiagnosticsCollectionMode.Event:
							collected.Add(state.PluginData);
							break;
						case DiagnosticsCollectionMode.Snapshot:
							collected.RemoveWhere(x => x.EventName == state.PluginData.EventName);
							collected.Add(state.PluginData);
							break;
						case DiagnosticsCollectionMode.Partial:
							var events = collected.Where(x => x.EventName == state.PluginData.EventName).ToArray();

							// if no event exists, create new
							if (events.Length == 0)
								collected.Add(state.PluginData);
							else {
								// update all collected events
								foreach (var evt in events) {
									foreach (var (key, value) in state.PluginData.Data)
										evt.Data[key] = value;
								}
							}

							break;
					}

					if (collected.Count > state.Capacity)
						collected.Remove(collected.Min);

					return collected;
				},
				(PluginData: pluginData, Capacity: capacity)
			);

			try {
				onEventCollected?.Invoke(pluginData);
			}
			catch {
				// stay on target
			}
		});
	}

	MultiSourceDiagnosticsListener Listener { get; }

	ConcurrentDictionary<string, SortedSet<DiagnosticsData>> CollectedEventsByPlugin { get; } = new();

	public IEnumerable<DiagnosticsData> CollectedEvents(string source) =>
		CollectedEventsByPlugin.TryGetValue(source, out var data) ? data : Array.Empty<DiagnosticsData>();

	public bool HasCollectedEvents(string source) =>
		CollectedEventsByPlugin.TryGetValue(source, out var data) && data.Count > 0;

	public void ClearCollectedEvents(string source) {
		if (CollectedEventsByPlugin.TryGetValue(source, out var data))
			data.Clear();
	}

	public void ClearAllCollectedEvents() {
		foreach (var data in CollectedEventsByPlugin.Values)
			data.Clear();
	}

	public void Dispose() {
		Listener.Dispose();
		CollectedEventsByPlugin.Clear();
	}

	/// <summary>
	///     Starts the <see cref="DiagnosticsDataCollector" /> with the specified delegate and sources.
	///     This method is a convenient way to create a new instance of the <see cref="DiagnosticsDataCollector" /> and start collecting data immediately.
	/// </summary>
	/// <param name="onEventCollected">A delegate to handle <see cref="DiagnosticsData" /> events.</param>
	/// <param name="sources">The plugin diagnostic names to collect diagnostics data from.</param>
	public static DiagnosticsDataCollector Start(OnEventCollected onEventCollected, params string[] sources) =>
			new(sources, 10, onEventCollected);

	/// <summary>
	///     Starts the <see cref="DiagnosticsDataCollector" /> with the specified delegate and sources.
	///     This method is a convenient way to create a new instance of the <see cref="DiagnosticsDataCollector" /> and start collecting data immediately.
	/// </summary>
	/// <param name="onEventCollected">A delegate to handle <see cref="DiagnosticsData" /> events.</param>
	/// <param name="capacity">The maximum number of diagnostics data to collect per source.</param>
	/// <param name="sources">The plugin diagnostic names to collect diagnostics data from.</param>
	public static DiagnosticsDataCollector Start(OnEventCollected onEventCollected, int capacity, params string[] sources) =>
		new(sources, capacity, onEventCollected);

	/// <summary>
	///     Starts the <see cref="DiagnosticsDataCollector" /> with the specified sources.
	///     This method is a convenient way to create a new instance of the <see cref="DiagnosticsDataCollector" /> and start collecting data immediately.
	/// </summary>
	/// <param name="sources">
	///     The plugin diagnostic names to collect diagnostics data from.
	/// </param>
	public static DiagnosticsDataCollector Start(params string[] sources) => new(sources);

	/// <summary>
	///     Starts the <see cref="DiagnosticsDataCollector" /> with the specified sources.
	///     This method is a convenient way to create a new instance of the <see cref="DiagnosticsDataCollector" /> and start collecting data immediately.
	/// </summary>
	/// <param name="capacity">The maximum number of diagnostics data to collect per source.</param>
	/// <param name="sources">The plugin diagnostic names to collect diagnostics data from.</param>
	public static DiagnosticsDataCollector Start(int capacity, params string[] sources) => new(sources, capacity);
}