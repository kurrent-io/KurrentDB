// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace Kurrent.Kontext.Mcp.Model;

// Without polymorphism metadata STJ serializes via the DECLARED type's contract, so a Lean hit travelling
// as RecalledMemory would emit only the base's `score` and silently drop the body. The discriminator
// makes the runtime arm explicit on the wire: {"kind":"lean","score":0.87,"memory":{...}}.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Lean), "lean")]
[JsonDerivedType(typeof(Full), "full")]
public abstract class RecalledMemory {
	// Private ctor closes the hierarchy to the two arms below (see Citation for the same pattern).
	RecalledMemory() { }

	public double Score { get; set; } = 0;

	public sealed class Lean : RecalledMemory {
		public LeanMemory Memory { get; set; } = new();
	}

	public sealed class Full : RecalledMemory {
		public StoredMemory Memory { get; set; } = new();
	}
}

public sealed class LeanMemory {
	public string MemoryId { get; set; } = "";

	public MemoryType MemoryType { get; set; } = MemoryType.Observation;

	public string Content { get; set; } = "";

	public IReadOnlyList<Tag> Tags { get; set; } = [];

	public MemoryImportance Importance { get; set; } = MemoryImportance.Normal;

	public DateTimeOffset RetainedAt { get; set; }
}
