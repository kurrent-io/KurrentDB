// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace KurrentDB.Kontext.Mcp.Memory;

public readonly record struct FactPayload(string Fact, string[] Keywords, SourceEvent[] SourceEvents, DateTime RetainedAt) {
	public static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	public static FactPayload Parse(ReadOnlyMemory<byte> data) {
		try {
			return JsonSerializer.Deserialize<FactPayload>(data.Span, JsonOptions);
		} catch (JsonException) {
			return default;
		}
	}
}