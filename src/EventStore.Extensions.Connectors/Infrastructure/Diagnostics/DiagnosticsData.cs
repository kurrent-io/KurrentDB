namespace EventStore.Connectors.Diagnostics;

/// <summary>
///		Represents the mode of collection for a diagnostics event.
/// </summary>
public enum DiagnosticsCollectionMode {
	/// <summary>
	///		Appends multiple events regardless or their type.
	/// </summary>
	Event,

	/// <summary>
	///		Overrides previously collected event.
	/// </summary>
	Snapshot,

	/// <summary>
	///		Merges with previously collected event.
	/// </summary>
	Partial
}

/// <summary>
///     Represents diagnostic data of a component.
/// </summary>
public readonly record struct DiagnosticsData() : IComparable<DiagnosticsData>, IComparable {
	public static DiagnosticsData None { get; } = new() { Data = null! };

	/// <summary>
	///		The source of the event that matches the DiagnosticsName.
	/// </summary>
	public string Source { get; init; } = string.Empty;

	/// <summary>
	///		The name of the event. The default is PluginDiagnosticsData.
	/// </summary>
	public string EventName { get; init; } = nameof(DiagnosticsData);

	/// <summary>
	///		The data associated with the event in the form of a dictionary.
	/// </summary>
	public required Dictionary<string, object?> Data { get; init; }

	/// <summary>
	///		When the event occurred.
	/// </summary>
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

	/// <summary>
	///		Represents the mode of data collection for a plugin event.
	/// </summary>
	public DiagnosticsCollectionMode CollectionMode { get; init; } = DiagnosticsCollectionMode.Event;

	/// <summary>
	///		Gets the value associated with the specified key.
	/// </summary>
	public T GetValue<T>(string key, T defaultValue) =>
		Data.TryGetValue(key, out var value) &&
		value is T typedValue ? typedValue : defaultValue;

	/// <summary>
	///		Gets the value associated with the specified key.
	/// </summary>
	public T? GetValue<T>(string key) =>
		Data.TryGetValue(key, out var value) ? (T?)value : default;

	public int CompareTo(DiagnosticsData other) {
		var sourceComparison = string.Compare(Source, other.Source, StringComparison.Ordinal);
		if (sourceComparison != 0) return sourceComparison;

		var eventNameComparison = string.Compare(EventName, other.EventName, StringComparison.Ordinal);
		if (eventNameComparison != 0) return eventNameComparison;

		return Timestamp.CompareTo(other.Timestamp);
	}

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj)) return 1;

		return obj is DiagnosticsData other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(DiagnosticsData)}");
	}

	public static bool operator <(DiagnosticsData left, DiagnosticsData right) => left.CompareTo(right) < 0;

	public static bool operator >(DiagnosticsData left, DiagnosticsData right) => left.CompareTo(right) > 0;

	public static bool operator <=(DiagnosticsData left, DiagnosticsData right) => left.CompareTo(right) <= 0;

	public static bool operator >=(DiagnosticsData left, DiagnosticsData right) => left.CompareTo(right) >= 0;
}