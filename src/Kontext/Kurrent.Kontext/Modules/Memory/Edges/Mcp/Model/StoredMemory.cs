// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Memory.Mcp.Model;

public sealed class StoredMemory {
	public string MemoryId { get; set; } = "";

	public MemoryType MemoryType { get; set; }

	public string Content { get; set; } = "";

	public MemoryImportance Importance { get; set; }

	public string Reasoning { get; set; } = "";

	public IReadOnlyList<Evidence> Evidence { get; set; } = [];

	public IReadOnlyList<Tag> Tags { get; set; } = [];

	public TemporalContext? ContentTime { get; set; }

	public IReadOnlyList<string> Supersedes { get; set; } = [];

	public DateTimeOffset RetainedAt { get; set; }

	public DateTimeOffset? LastAccessedAt { get; set; }

	public DateTimeOffset? SupersededAt { get; set; }

	public string? SupersededBy { get; set; }
}
