// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Checker;

/// <summary>
/// One line of a node's log.json.
/// </summary>
/// <remarks>
/// KurrentDB already writes structured logs in Serilog's compact format
/// (<c>{ {@t, @mt, @r, @l, @i, @x, ..@p} }</c>), so the invariants match on the message
/// <b>template</b> and read typed properties, rather than scraping rendered text.
/// </remarks>
public sealed record LogEvent(
	string Node,
	DateTimeOffset Timestamp,
	string Template,
	string Level,
	string? Exception,
	IReadOnlyDictionary<string, JsonElement> Properties) {

	public bool TemplateContains(string fragment) =>
		Template.Contains(fragment, StringComparison.Ordinal);

	public string? String(string property) =>
		Properties.TryGetValue(property, out var value)
			? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
			: null;

	public long? Number(string property) {
		if (!Properties.TryGetValue(property, out var value))
			return null;

		return value.ValueKind switch {
			JsonValueKind.Number => value.TryGetInt64(out var n) ? n : null,
			JsonValueKind.String => long.TryParse(value.GetString(), out var n) ? n : null,
			_ => null,
		};
	}
}

public static class NodeLogs {
	// Serilog's compact format omits @l for Information, which is the level most of the
	// interesting lines are logged at.
	private const string DefaultLevel = "Information";

	/// <summary>
	/// Reads every node's log.json. Each node's logs are mounted under
	/// <paramref name="logsRoot"/>/&lt;node&gt;/, with the server adding its own component
	/// subdirectory below that, so the node name is the first path segment.
	/// </summary>
	public static IReadOnlyList<LogEvent> Read(string logsRoot) {
		var events = new List<LogEvent>();

		if (!Directory.Exists(logsRoot)) {
			Console.WriteLine($"[logs] {logsRoot} does not exist - no log invariants can be checked");
			return events;
		}

		foreach (var nodeDir in Directory.EnumerateDirectories(logsRoot).OrderBy(d => d)) {
			var node = Path.GetFileName(nodeDir);
			var files = Directory.EnumerateFiles(nodeDir, "log*.json", SearchOption.AllDirectories)
				.Where(f => !Path.GetFileName(f).StartsWith("log-err", StringComparison.Ordinal) &&
				            !Path.GetFileName(f).StartsWith("log-stats", StringComparison.Ordinal))
				.OrderBy(f => f)
				.ToArray();

			if (files.Length == 0)
				Console.WriteLine($"[logs] {node}: no log.json found under {nodeDir}");

			foreach (var file in files)
				events.AddRange(ReadFile(node, file));
		}

		return events;
	}

	private static IEnumerable<LogEvent> ReadFile(string node, string path) {
		// The server may still hold the file open, so share the write handle.
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);

		var parsed = new List<LogEvent>();
		while (reader.ReadLine() is { } line) {
			if (line.Length == 0)
				continue;

			LogEvent? entry;
			try {
				entry = Parse(node, line);
			} catch (JsonException) {
				// A torn final line while the node is still running. Skip it.
				continue;
			}

			if (entry is not null)
				parsed.Add(entry);
		}

		return parsed;
	}

	private static LogEvent? Parse(string node, string line) {
		using var document = JsonDocument.Parse(line);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
			return null;

		DateTimeOffset timestamp = default;
		var template = "";
		var level = DefaultLevel;
		string? exception = null;
		var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

		foreach (var property in root.EnumerateObject()) {
			switch (property.Name) {
				case "@t":
					DateTimeOffset.TryParse(property.Value.GetString(), out timestamp);
					break;
				case "@mt":
					template = property.Value.GetString() ?? "";
					break;
				case "@l":
					level = property.Value.GetString() ?? DefaultLevel;
					break;
				case "@x":
					exception = property.Value.GetString();
					break;
				case "@r":
				case "@i":
					break;
				default:
					properties[property.Name] = property.Value.Clone();
					break;
			}
		}

		return new LogEvent(node, timestamp, template, level, exception, properties);
	}
}
