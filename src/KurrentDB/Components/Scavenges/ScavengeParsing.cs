// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;

namespace KurrentDB.Components.Scavenges;

// Pure mapping from raw $scavenges-<id> event payloads to display rows. No IPublisher/IO, so it can
// be unit-tested directly. Status/result wording mirrors the legacy Angular scavenge view.
public static class ScavengeParsing {
	public static ScavengeDetailEvent MapDetailEvent(string eventType, JsonElement root) {
		var detail = new ScavengeDetailEvent();
		switch (eventType) {
			case "$scavengeStarted":
				detail.Status = "Started";
				break;
			case "$scavengeChunksCompleted": {
				var start = GetInt(root, "chunkStartNumber");
				var end = GetInt(root, "chunkEndNumber");
				detail.Status = $"Scavenging chunks {start} - {end} complete";
				detail.SpaceSaved = GetLong(root, "spaceSaved");
				detail.TimeTaken = FormatTimeTaken(root);
				detail.Result = ChunkResult(root, "wasScavenged", end - start + 1, "scavenged", "No chunks scavenged");
				break;
			}
			case "$scavengeMergeCompleted": {
				var start = GetInt(root, "chunkStartNumber");
				var end = GetInt(root, "chunkEndNumber");
				detail.Status = $"Merging chunks {start} - {end} complete";
				detail.SpaceSaved = GetLong(root, "spaceSaved");
				detail.TimeTaken = FormatTimeTaken(root);
				detail.Result = ChunkResult(root, "wasMerged", end - start + 1, "merged", "No chunks merged");
				break;
			}
			case "$scavengeIndexCompleted": {
				var level = GetInt(root, "level");
				var index = GetInt(root, "index");
				detail.Status = $"Scavenging index table ({level},{index}) complete";
				detail.SpaceSaved = GetLong(root, "spaceSaved");
				detail.TimeTaken = FormatTimeTaken(root);
				detail.Result = IndexResult(root);
				break;
			}
			case "$scavengeCompleted": {
				detail.Status = "Completed";
				detail.SpaceSaved = GetLong(root, "spaceSaved");
				detail.TimeTaken = FormatTimeTaken(root);
				var completed = root.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";
				if (completed == "Failed" && root.TryGetProperty("error", out var err))
					completed += ": " + err.GetString();
				detail.Result = completed;
				break;
			}
			default:
				detail.Status = eventType;
				break;
		}
		return detail;
	}

	// errorMessage (when present) takes precedence over the success/none text, matching Angular.
	static string ChunkResult(JsonElement root, string flag, int count, string verb, string noneText) {
		if (TryGetNonEmptyString(root, "errorMessage", out var error))
			return "Error: " + error;
		if (root.TryGetProperty(flag, out var f) && f.GetBoolean())
			return $"{count} chunk(s) {verb}";
		return noneText;
	}

	static string IndexResult(JsonElement root) {
		if (TryGetNonEmptyString(root, "errorMessage", out var error))
			return "Error: " + error;
		if (root.TryGetProperty("wasScavenged", out var ws) && ws.GetBoolean())
			return $"{GetLong(root, "entriesDeleted")} index entries scavenged";
		return "Index table not scavenged";
	}

	public static string FormatTimeTaken(JsonElement root) {
		if (!root.TryGetProperty("timeTaken", out var prop))
			return "";
		var raw = prop.GetString();
		return TimeSpan.TryParse(raw, out var ts) ? ts.ToString(@"h\:mm\:ss\.fff") : raw ?? "";
	}

	static bool TryGetNonEmptyString(JsonElement root, string name, out string value) {
		value = root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
		return !string.IsNullOrEmpty(value);
	}

	static int GetInt(JsonElement root, string name) =>
		root.TryGetProperty(name, out var p) ? p.GetInt32() : 0;

	static long GetLong(JsonElement root, string name) =>
		root.TryGetProperty(name, out var p) ? p.GetInt64() : 0;
}
