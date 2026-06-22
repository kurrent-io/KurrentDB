// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace KurrentDB.Kontext.Mcp.Inquiry;

/// <summary>Reference to a contiguous run of events within a stream, starting at <c>EventNumber</c>.</summary>
public record EventRef {
	[JsonPropertyName("stream"), Description("Stream name")]
	public string Stream { get; init; } = "";

	[JsonPropertyName("eventNumber"), Description("First event number (0-based)")]
	public long EventNumber { get; init; }

	[JsonPropertyName("count"), Description("Number of events. Defaults to 1.")]
	public int Count { get; init; } = 1;
}