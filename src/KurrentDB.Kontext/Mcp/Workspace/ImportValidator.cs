// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace KurrentDB.Kontext.Mcp.Workspace;

public sealed record ImportError(int Index, string Message);

public static class ImportValidator {
	public static async Task<(IReadOnlyList<PendingEvent> Valid, IReadOnlyList<ImportError> Errors)>
		Validate(
			ImportEvent[] events,
			IReadOnlyList<string> allowedStreamPrefixes,
			Func<string, ValueTask<bool>> canWriteStream) {
		var errors = new List<ImportError>();
		var valid = new List<PendingEvent>();
		var writeAccess = new Dictionary<string, bool>(StringComparer.Ordinal);

		for (var i = 0; i < events.Length; i++) {
			var e = events[i];

			if (string.IsNullOrWhiteSpace(e.Stream)) {
				errors.Add(new ImportError(i, "missing stream name."));
				continue;
			}

			if (e.Stream.StartsWith('$')) {
				errors.Add(new ImportError(i, $"stream '{e.Stream}' may not start with '$' (system stream)."));
				continue;
			}

			if (allowedStreamPrefixes.Count > 0
				&& !allowedStreamPrefixes.Any(p => e.Stream.StartsWith(p, StringComparison.Ordinal))) {
				errors.Add(new ImportError(i, $"stream '{e.Stream}' does not match any allowed prefix."));
				continue;
			}

			if (string.IsNullOrWhiteSpace(e.EventType)) {
				errors.Add(new ImportError(i, "missing event type."));
				continue;
			}

			if (e.Data.ValueKind == JsonValueKind.Undefined) {
				errors.Add(new ImportError(i, "missing data payload."));
				continue;
			}

			if (!writeAccess.TryGetValue(e.Stream, out var allowed)) {
				allowed = await canWriteStream(e.Stream);
				writeAccess[e.Stream] = allowed;
			}

			if (!allowed) {
				errors.Add(new ImportError(i, $"write access denied for stream '{e.Stream}'."));
				continue;
			}

			var data = JsonSerializer.SerializeToUtf8Bytes(e.Data);
			valid.Add(new PendingEvent(e.Stream, e.EventType, data));
		}

		return (valid, errors);
	}

	public static string FormatResult(
		IReadOnlyList<PendingEvent> valid,
		IReadOnlyList<ImportError> errors) {
		var streamCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var e in valid) {
			streamCounts.TryGetValue(e.Stream, out var count);
			streamCounts[e.Stream] = count + 1;
		}

		var streams = new object[streamCounts.Count];
		var s = 0;
		foreach (var (stream, count) in streamCounts)
			streams[s++] = new { stream, events = count };

		object[]? errorObjs = null;
		if (errors.Count > 0) {
			errorObjs = new object[errors.Count];
			for (var i = 0; i < errors.Count; i++)
				errorObjs[i] = new { index = errors[i].Index, message = errors[i].Message };
		}

		var result = new { imported = valid.Count, streams, errors = errorObjs };
		return JsonSerializer.Serialize(result, JsonOptions.Compact);
	}
}
