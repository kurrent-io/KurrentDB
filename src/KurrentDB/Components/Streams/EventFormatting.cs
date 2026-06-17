// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Text.Json;

namespace KurrentDB.Components.Streams;

public static class EventFormatting {
	static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

	// Non-JSON payloads are shown as a short preview only: event data can be up to ~16MB, and dumping
	// that raw into a <pre> would be unusable and heavy to push over the SignalR circuit.
	const int MaxNonJsonPreviewChars = 200;

	// Pretty-print event payloads as JSON, falling back to a truncated raw-UTF-8 preview for non-JSON data.
	public static string FormatJson(ReadOnlyMemory<byte> bytes) {
		try {
			using var doc = JsonDocument.Parse(bytes);
			return JsonSerializer.Serialize(doc, JsonOpts);
		} catch {
			// Decode only a bounded prefix so we never materialise a huge string just to truncate it.
			// 1KB guarantees at least MaxNonJsonPreviewChars characters even for multi-byte UTF-8.
			var span = bytes.Length > 1024 ? bytes.Span[..1024] : bytes.Span;
			var text = Encoding.UTF8.GetString(span);
			return text.Length > MaxNonJsonPreviewChars ? text[..MaxNonJsonPreviewChars] + "..." : text;
		}
	}
}
