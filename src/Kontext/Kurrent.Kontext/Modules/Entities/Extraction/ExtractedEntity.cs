// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// The POLE+O entity types (Person, Object, Location, Event, Organization). Types are open
/// strings — an extractor may emit others — but these are the ones the shipped extractors map to.
/// </summary>
public static class EntityTypes {
	public const string Person       = "PERSON";
	public const string Object       = "OBJECT";
	public const string Location     = "LOCATION";
	public const string Event        = "EVENT";
	public const string Organization = "ORGANIZATION";
}

/// <summary>
/// One entity occurrence as an extractor saw it in the text — the surface form, its type, where
/// it sat, and how sure the stage was. Resolution decides later which stored entity it IS.
/// </summary>
public sealed record ExtractedEntity {
	/// <summary>The surface form exactly as it appeared in the text.</summary>
	public required string Name { get; init; }

	/// <summary>The entity type, uppercase by convention (see <see cref="EntityTypes"/>).</summary>
	public required string Type { get; init; }

	/// <summary>Optional finer classification (e.g. <c>URL</c> for an OBJECT).</summary>
	public string? Subtype { get; init; }

	/// <summary>The stage's confidence in this occurrence, in [0, 1].</summary>
	public double Confidence { get; init; } = 1.0;

	/// <summary>Inclusive character offset of the first character in the source text, when the stage knows it.</summary>
	public int? Start { get; init; }

	/// <summary>Exclusive character offset one past the last character, when the stage knows it.</summary>
	public int? End { get; init; }

	/// <summary>The name of the extractor stage that produced this occurrence.</summary>
	public string? Extractor { get; init; }

	/// <summary>The canonical matching form of <see cref="Name"/> (see <see cref="EntityName.Normalize"/>).</summary>
	public string NormalizedName => EntityName.Normalize(Name);

	/// <summary>Type and subtype as one string (<c>OBJECT:URL</c>), or just the type.</summary>
	public string FullType => string.IsNullOrEmpty(Subtype) ? Type : $"{Type}:{Subtype}";

	/// <summary>
	/// The identity key merge strategies deduplicate on: same normalized name AND same type.
	/// Two types never collapse into one entity, no matter how similar the names.
	/// </summary>
	public string Key => $"{NormalizedName}::{Type}";
}
