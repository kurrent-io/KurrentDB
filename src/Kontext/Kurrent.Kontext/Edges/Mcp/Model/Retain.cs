// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

public sealed class RetainResult {
	public IReadOnlyList<RetainedMemory> Results { get; set; } = [];
}

public sealed class RetainedMemory {
	public string MemoryId { get; set; } = "";

	public IReadOnlyList<RelatedMemory> Related { get; set; } = [];
}

public sealed class RelatedMemory {
	public string MemoryId { get; set; } = "";

	public double Similarity { get; set; }
}
