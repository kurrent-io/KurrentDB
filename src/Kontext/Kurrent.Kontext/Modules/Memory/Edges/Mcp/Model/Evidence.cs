// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace Kurrent.Kontext.Memory.Mcp.Model;

// Evidence arrives as INPUT (retain's evidence), and deserializing into an abstract type without
// polymorphism metadata throws outright — the discriminator is what lets an agent submit
// {"kind":"memory","memory":{...}} or {"kind":"git","git":{...}} at all.
//
// The contract's `generic` arm is deliberately NOT exposed here. It wraps an untyped struct for
// sources nothing resolves yet, which an agent cannot fill usefully and which would put a free-form
// bag in the generated tool schema; it stays available on the gRPC edge.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ToMemory), "memory")]
[JsonDerivedType(typeof(ToRecord), "record")]
[JsonDerivedType(typeof(ToGit), "git")]
[JsonDerivedType(typeof(ToWeb), "web")]
public abstract class Evidence {
	// Private ctor closes the hierarchy to the arms nested below (a poor-man's discriminated union;
	// the repo has no shared Variant type). Pattern-match with `evidence switch { Evidence.ToMemory m => … }`.
	Evidence() { }

	public sealed class ToMemory : Evidence {
		public MemoryRef Memory { get; set; } = new();
	}

	public sealed class ToRecord : Evidence {
		public RecordRef Record { get; set; } = new();
	}

	public sealed class ToGit : Evidence {
		public GitRef Git { get; set; } = new();
	}

	public sealed class ToWeb : Evidence {
		public WebRef Web { get; set; } = new();
	}
}

public sealed class MemoryRef {
	public string Id { get; set; } = "";
}

public sealed class RecordRef {
	public string Id { get; set; } = "";

	public long Position { get; set; }
}

public sealed class GitRef {
	public string? Repo { get; set; }

	public string Commit { get; set; } = "";

	public string? Branch { get; set; }

	public string? Path { get; set; }

	public string? Symbol { get; set; }

	public int? LineStart { get; set; }

	public int? LineEnd { get; set; }

	public string? Excerpt { get; set; }
}

public sealed class WebRef {
	public string Uri { get; set; } = "";

	public string? Title { get; set; }

	public IReadOnlyList<string> Excerpts { get; set; } = [];

	public DateTimeOffset? RetrievedAt { get; set; }
}
