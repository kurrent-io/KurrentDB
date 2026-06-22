// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.Json;

namespace KurrentDB.Kontext.Events;

/// <summary>
/// Helpers that turn an event (type + JSON payload) into text representations.
/// </summary>
internal static class EventTextHelpers {
	const int MaxDepth = 128; // bumped above System.Text.Json's default of 64.
	static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = MaxDepth };

	/// <summary>
	/// Splits a name (PascalCase, camelCase, snake_case, kebab-case) into lowercase
	/// space-separated words.
	/// </summary>
	internal static string SplitName(string name) {
		var sb = new StringBuilder(name.Length);
		AppendSplitName(name, sb);
		return sb.ToString().Trim();
	}

	/// <summary>
	/// Renders the event as YAML-ish text: split event type, then the JSON data as YAML.
	/// Caller supplies <paramref name="buffer"/> for reuse across events.
	/// </summary>
	internal static string BuildEventText(string eventType, ReadOnlySpan<byte> data, StringBuilder buffer) {
		buffer.Clear();
		AppendSplitName(eventType, buffer);
		TryWriteJsonAsYaml(data, buffer);
		return buffer.ToString().Trim();
	}

	/// <summary>
	/// Splits the event into a flat keys text (event type + every JSON property name,
	/// regardless of nesting) and a flat values text (every leaf scalar; booleans /
	/// nulls / whitespace skipped). Caller supplies the StringBuilders for reuse.
	/// </summary>
	internal static (string Keys, string Values) BuildFlatKeyValueTexts(
		string eventType, ReadOnlySpan<byte> data, StringBuilder keysBuffer, StringBuilder valuesBuffer) {
		keysBuffer.Clear();
		valuesBuffer.Clear();
		AppendSplitName(eventType, keysBuffer);
		TryWriteJsonAsKeysAndValues(data, keysBuffer, valuesBuffer);
		return (keysBuffer.ToString().Trim(), valuesBuffer.ToString().Trim());
	}

	// -------- Internal walkers --------

	static void TryWriteJsonAsYaml(ReadOnlySpan<byte> data, StringBuilder sb) {
		if (data.IsEmpty)
			return;
		try {
			var reader = new Utf8JsonReader(data, ReaderOptions);
			WriteYaml(ref reader, sb);
		} catch (JsonException) { /* invalid JSON — leave the eventType-only output */ }
	}

	static void TryWriteJsonAsKeysAndValues(ReadOnlySpan<byte> data, StringBuilder keys, StringBuilder values) {
		if (data.IsEmpty)
			return;
		try {
			var reader = new Utf8JsonReader(data, ReaderOptions);
			WriteKeysAndValues(ref reader, keys, values);
		} catch (JsonException) { /* invalid JSON — leave only the eventType in keys */ }
	}

	/// <summary>
	/// Walks the reader once and emits a YAML-ish rendering. Tracks per-depth whether the
	/// containing scope is an array (children get a <c>- </c> prefix) or an object (children
	/// are <c>key: value</c>). The most-recently-read PropertyName is held in
	/// <c>pendingKey</c> until consumed by the next value/container.
	/// </summary>
	static void WriteYaml(ref Utf8JsonReader reader, StringBuilder sb) {
		// isArrayAt[d] = "is the container at depth d an array?". Depth 0 means "no container
		// above" — index 0 is unused as a container flag but still safe to access. Sized
		// MaxDepth + 1 to give a slot for every depth 1..MaxDepth without -1 arithmetic.
		Span<bool> isArrayAt = stackalloc bool[MaxDepth + 1];
		var depth = 0;
		string? pendingKey = null;

		while (reader.Read()) {
			switch (reader.TokenType) {
				case JsonTokenType.StartObject:
				case JsonTokenType.StartArray:
					var isArray = reader.TokenType == JsonTokenType.StartArray;
					// Root container has no header.
					if (depth > 0)
						EmitContainerHeader(sb, depth, isArrayAt[depth], pendingKey);
					depth++;
					isArrayAt[depth] = isArray;
					pendingKey = null;
					break;

				case JsonTokenType.EndObject:
				case JsonTokenType.EndArray:
					depth--;
					break;

				case JsonTokenType.PropertyName:
					pendingKey = reader.GetString();
					break;

				case JsonTokenType.String:
					EmitScalar(sb, depth, isArrayAt[depth], pendingKey, reader.GetString() ?? "");
					pendingKey = null;
					break;

				case JsonTokenType.Number:
					EmitNumberScalar(sb, depth, isArrayAt[depth], pendingKey, reader.ValueSpan);
					pendingKey = null;
					break;

				case JsonTokenType.True:
					EmitScalar(sb, depth, isArrayAt[depth], pendingKey, "true");
					pendingKey = null;
					break;

				case JsonTokenType.False:
					EmitScalar(sb, depth, isArrayAt[depth], pendingKey, "false");
					pendingKey = null;
					break;

				case JsonTokenType.Null:
					EmitScalar(sb, depth, isArrayAt[depth], pendingKey, "null");
					pendingKey = null;
					break;
			}
		}
	}

	/// <summary>
	/// Walks the reader once and emits flat keys/values. No depth tracking — keys collects
	/// every PropertyName regardless of nesting, values collects every leaf scalar.
	/// </summary>
	static void WriteKeysAndValues(ref Utf8JsonReader reader, StringBuilder keys, StringBuilder values) {
		while (reader.Read()) {
			switch (reader.TokenType) {
				case JsonTokenType.PropertyName:
					keys.Append(' ');
					AppendSplitName(reader.GetString()!, keys);
					break;

				case JsonTokenType.String:
					var s = reader.GetString();
					if (!string.IsNullOrWhiteSpace(s)) {
						values.Append(' ');
						values.Append(s);
					}
					break;

				case JsonTokenType.Number:
					values.Append(' ');
					AppendUtf8Ascii(reader.ValueSpan, values);
					break;

					// True/False/Null skipped — they're stop-wordy noise downstream.
			}
		}
	}

	// -------- Emit helpers --------

	static void EmitContainerHeader(StringBuilder sb, int depth, bool parentIsArray, string? pendingKey) {
		sb.AppendLine();
		AppendIndent(sb, depth - 1);
		if (parentIsArray) {
			// Array entries that are themselves containers get a bare '-' line; the children
			// appear at depth+1 indent.
			sb.Append('-');
		} else {
			sb.Append(pendingKey);
			sb.Append(':');
		}
	}

	static void EmitScalar(StringBuilder sb, int depth, bool inArray, string? pendingKey, string value) {
		sb.AppendLine();
		AppendIndent(sb, depth - 1);
		if (depth == 0) {
			// Root primitive — no parent key or array context, emit bare value.
		} else if (inArray) {
			sb.Append("- ");
		} else {
			sb.Append(pendingKey);
			sb.Append(": ");
		}
		sb.Append(value);
	}

	static void EmitNumberScalar(StringBuilder sb, int depth, bool inArray, string? pendingKey, ReadOnlySpan<byte> numberBytes) {
		sb.AppendLine();
		AppendIndent(sb, depth - 1);
		if (depth == 0) {
			// Root primitive — no parent key or array context, emit bare value.
		} else if (inArray) {
			sb.Append("- ");
		} else {
			sb.Append(pendingKey);
			sb.Append(": ");
		}
		AppendUtf8Ascii(numberBytes, sb);
	}

	static void AppendIndent(StringBuilder sb, int level) {
		for (var i = 0; i < level * 2; i++)
			sb.Append(' ');
	}

	/// <summary>
	/// Appends a name to <paramref name="dest"/>, splitting PascalCase / camelCase /
	/// snake_case / kebab-case into lowercase space-separated words. Writes directly into
	/// the buffer — no intermediate string allocation.
	/// </summary>
	internal static void AppendSplitName(string name, StringBuilder dest) {
		for (var i = 0; i < name.Length; i++) {
			var c = name[i];
			if (c == '_' || c == '-') {
				dest.Append(' ');
			} else if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) {
				dest.Append(' ');
				dest.Append(char.ToLowerInvariant(c));
			} else {
				dest.Append(char.ToLowerInvariant(c));
			}
		}
	}

	/// <summary>
	/// Appends an ASCII-only UTF-8 byte sequence (e.g. a JSON number token) to a
	/// StringBuilder via a 1-to-1 byte→char cast. Avoids a full UTF-8 decode.
	/// </summary>
	static void AppendUtf8Ascii(ReadOnlySpan<byte> ascii, StringBuilder dest) {
		// Common case is short (a few bytes for typical numbers); stackalloc avoids the alloc
		// and lets us pass a single ReadOnlySpan<char> in to StringBuilder.Append.
		Span<char> buf = ascii.Length <= 64 ? stackalloc char[64] : new char[ascii.Length];
		for (var i = 0; i < ascii.Length; i++)
			buf[i] = (char)ascii[i];
		dest.Append(buf[..ascii.Length]);
	}
}