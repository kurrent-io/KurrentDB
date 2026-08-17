// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// What one extraction pass produced over one text. Entities only, on purpose: Kontext derives
/// relatedness at query time — from shared mentions and the pending-link hop — instead of
/// extracting and storing edges nothing reads.
/// </summary>
public sealed record ExtractionResult {
	public static readonly ExtractionResult Empty = new();

	public IReadOnlyList<ExtractedEntity> Entities { get; init; } = [];

	/// <summary>Drops occurrences whose surface form fails <see cref="EntityName.IsValid"/>.</summary>
	public ExtractionResult FilterInvalid() =>
		this with { Entities = [.. Entities.Where(entity => EntityName.IsValid(entity.Name))] };
}
