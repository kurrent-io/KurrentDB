// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

public sealed class Memory {
	public string? Id { get; set; }

	public MemoryType Type { get; set; } = MemoryType.Observation;

	public string Content { get; set; } = "";

	public MemoryImportance Importance { get; set; } = MemoryImportance.Normal;

	public Evidence? Evidence { get; set; }

	public IReadOnlyList<Tag> Tags { get; set; } = [];

	public MemorySentiment Sentiment { get; set; } = MemorySentiment.Neutral;

	public MemoryUrgency Urgency { get; set; } = MemoryUrgency.Medium;

	public TemporalContext? Validity { get; set; }

	public IReadOnlyList<string> Supersedes { get; set; } = [];
}
