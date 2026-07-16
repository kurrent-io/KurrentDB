// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// What a memory rests on: the reasoning that produced it together with the sources it cites. Derived
/// memories carry it; a raw <see cref="MemoryType.Observation"/> rests on nothing and is <see cref="None"/>.
/// Reasoning and citations travel together — the reasoning is the argument over exactly these citations —
/// so a memory carries both or neither.
/// </summary>
public sealed record Evidence {
	/// <summary>The empty evidence — a memory that rests on nothing (a raw observation). The null-object default.</summary>
	public static readonly Evidence None = new();

	/// <summary>
	/// The "because" tying the cited sources to this memory. Kept out of the memory's content on purpose, so
	/// the recall embedding indexes the memory itself, not the citation trail. Empty for a raw observation.
	/// </summary>
	public string Reasoning { get; init; } = "";

	/// <summary>The sources this memory cites as support, in citation order. Empty for a raw observation.</summary>
	public IReadOnlyList<Citation> Citations { get; init; } = [];
}

/// <summary>One cited source — exactly one arm: a cited memory, or a cited KurrentDB log record.</summary>
public abstract record Citation {
	// Private ctor closes the hierarchy to the two nested arms below (a poor-man's discriminated union;
	// the repo has no shared Variant type). Pattern-match with `citation switch { Citation.ToMemory m => … }`.
	Citation() { }

	/// <summary>A citation of another memory.</summary>
	public sealed record ToMemory(MemoryRef Memory) : Citation;

	/// <summary>A citation of a KurrentDB log record.</summary>
	public sealed record ToRecord(RecordRef Record) : Citation;
}

/// <summary>A pointer to another memory that supports this one.</summary>
/// <param name="Id">The cited memory id.</param>
/// <param name="Position">Its position in the log (exact, replayable).</param>
public readonly record struct MemoryRef(MemoryId Id, long Position);

/// <summary>A pointer to a KurrentDB log record that supports this memory.</summary>
/// <param name="Id">The record's id.</param>
/// <param name="Position">Its position in the log (exact, replayable).</param>
public readonly record struct RecordRef(string Id, long Position);
