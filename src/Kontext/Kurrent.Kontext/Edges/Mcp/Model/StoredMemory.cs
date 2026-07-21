// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

public sealed class StoredMemory {
	public string MemoryId { get; set; } = "";

	public MemoryType MemoryType { get; set; }

	public string Content { get; set; } = "";

	public MemoryImportance Importance { get; set; }

    public Evidence? Evidence { get; set; }

	public IReadOnlyList<Tag> Tags { get; set; } = [];

	public MemorySentiment Sentiment { get; set; } = MemorySentiment.Neutral;

	public MemoryUrgency Urgency { get; set; } = MemoryUrgency.Medium;

	public TemporalContext? Validity { get; set; }

	public IReadOnlyList<string> Supersedes { get; set; } = [];

	public DateTimeOffset RetainedAt { get; set; }

	public DateTimeOffset? LastAccessedAt { get; set; }

	public DateTimeOffset? RetractedAt { get; set; }

	public DateTimeOffset? SupersededAt { get; set; }

	public string? SupersededBy { get; set; }
}
