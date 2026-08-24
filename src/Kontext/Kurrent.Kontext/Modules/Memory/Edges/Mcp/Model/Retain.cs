// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Memory.Mcp.Model;

public sealed class RetainResult {
	public IReadOnlyList<RetainedMemory> Results { get; set; } = [];
}

public sealed class RetainedMemory {
	public RetainOutcome Outcome { get; set; }

	public string MemoryId { get; set; } = "";

	public IReadOnlyList<string> SupersededMemoryIds { get; set; } = [];

	public IReadOnlyList<RelatedMemory> Candidates { get; set; } = [];
}

public sealed class RelatedMemory {
	public double Distance { get; set; }

	public bool KeywordMatch { get; set; }

	// The lean projection, not just an id: without the content there is no way to tell a duplicate
	// from a contradiction without a reclaim round trip — which would also refresh the recency clock
	// of memories the server offered rather than the agent chose.
	public LeanMemory Memory { get; set; } = new();
}
