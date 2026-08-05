// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Mcp.Model;

// No id: the server mints every memory_id and returns them positionally, so a caller can neither
// collide with an existing memory nor cite one it is sending in the same batch.
public sealed class Memory {
	public MemoryType Type { get; set; } = MemoryType.Observation;

	public string Content { get; set; } = "";

	public MemoryImportance Importance { get; set; } = MemoryImportance.Normal;

	public string Reasoning { get; set; } = "";

	public IReadOnlyList<Evidence> Evidence { get; set; } = [];

	public IReadOnlyList<Tag> Tags { get; set; } = [];

	public TemporalContext? Validity { get; set; }

	public IReadOnlyList<string> Supersedes { get; set; } = [];
}
