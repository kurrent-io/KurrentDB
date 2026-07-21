// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace Kurrent.Kontext.Mcp.Model;

public sealed class Evidence {
	public string Reasoning { get; set; } = "";

	public IReadOnlyList<Citation> Citations { get; set; } = [];
}

// Citations arrive as INPUT (retain's evidence), and deserializing into an abstract type without
// polymorphism metadata throws outright — the discriminator is what lets an agent submit
// {"kind":"memory","memory":{...}} or {"kind":"record","record":{...}} at all.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ToMemory), "memory")]
[JsonDerivedType(typeof(ToRecord), "record")]
public abstract class Citation {
	// Private ctor closes the hierarchy to the two nested arms below (a poor-man's discriminated union;
	// the repo has no shared Variant type). Pattern-match with `citation switch { Citation.ToMemory m => … }`.
	Citation() { }

	public sealed class ToMemory : Citation {
		public MemoryRef Memory { get; set; } = new();
	}

	public sealed class ToRecord : Citation {
		public RecordRef Record { get; set; } = new();
	}
}

public sealed class MemoryRef {
	public string Id { get; set; } = "";

	public long? Position { get; set; }
}

public sealed class RecordRef {
	public string Id { get; set; } = "";

	public long Position { get; set; }
}
