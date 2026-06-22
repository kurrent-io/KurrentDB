// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;

namespace KurrentDB.Kontext.Mcp.Inquiry;

/// <summary>One event in an inquiry's working set, surfaced through the MCP tool results.</summary>
public sealed record InquiryEvent(
	[property: Description("Unique event identifier.")]
	long Id,
	[property: Description("Raw event payload (JSON).")]
	ReadOnlyMemory<byte> Data,
	[property: Description("Raw event metadata (JSON).")]
	ReadOnlyMemory<byte> Metadata,
	[property: Description("Stream name.")]
	string? Stream = null,
	[property: Description("Event number within the stream.")]
	long? EventNumber = null,
	[property: Description("Event type.")]
	string? EventType = null,
	[property: Description("UTC timestamp when the event was written.")]
	DateTime? Timestamp = null,
	[property: Description("Relevance score (only set for search-derived events).")]
	float? Score = null,
	[property: Description("True when the caller lacks read access to this event's stream; Data and Metadata are cleared.")]
	bool Redacted = false);