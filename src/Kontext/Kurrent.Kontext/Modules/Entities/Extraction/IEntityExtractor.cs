// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// One extraction stage: text in, entity occurrences out. Implementations must be safe to call
/// concurrently and deterministic for the same input — the projector replays them to rebuild
/// the read model.
/// </summary>
public interface IEntityExtractor {
	/// <summary>The stage's stable name, stamped on every occurrence it produces (provenance).</summary>
	string Name { get; }

	ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default);
}
