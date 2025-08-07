// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Diagnostics;

/// <summary>
///		Represents the mode of data collection for a plugin event.
/// </summary>
public enum PluginDiagnosticsDataCollectionMode {
	/// <summary>
	///		Appends multiple events regardless or their type.
	/// </summary>
	Event,
	
	/// <summary>
	///		Override previously collected event data.
	/// </summary>
	Snapshot,
	
	/// <summary>
	///		Merges with previously collected event data.
	/// </summary>
	Partial
}

/// <summary>
///     Represents diagnostic data of a plugin.
///     By default it is a snapshot and will override previously collected data, by event name.
/// </summary>
public readonly record struct PluginDiagnosticsData() : IComparable<PluginDiagnosticsData>, IComparable {
	public static PluginDiagnosticsData None { get; } = new() { Data = null! };

	/// <summary>
	///		The source of the event that matches the DiagnosticsName.
	/// </summary>
	public string Source { get; init; } = string.Empty;
	
	/// <summary>
	///		The name of the event. The default is PluginDiagnosticsData.
	/// </summary>
	public string EventName { get; init; } = nameof(PluginDiagnosticsData);
	
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
	public PluginDiagnosticsDataCollectionMode CollectionMode { get; init; } = PluginDiagnosticsDataCollectionMode.Event;
	
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
	
	public int CompareTo(PluginDiagnosticsData other) {
		var sourceComparison = string.Compare(Source, other.Source, StringComparison.Ordinal);
		if (sourceComparison != 0) return sourceComparison;

		var eventNameComparison = string.Compare(EventName, other.EventName, StringComparison.Ordinal);
		if (eventNameComparison != 0) return eventNameComparison;

		return Timestamp.CompareTo(other.Timestamp);
	}

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj)) return 1;

		return obj is PluginDiagnosticsData other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(PluginDiagnosticsData)}");
	}

	public static bool operator <(PluginDiagnosticsData left, PluginDiagnosticsData right) => left.CompareTo(right) < 0;

	public static bool operator >(PluginDiagnosticsData left, PluginDiagnosticsData right) => left.CompareTo(right) > 0;

	public static bool operator <=(PluginDiagnosticsData left, PluginDiagnosticsData right) => left.CompareTo(right) <= 0;

	public static bool operator >=(PluginDiagnosticsData left, PluginDiagnosticsData right) => left.CompareTo(right) >= 0;
}
