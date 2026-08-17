// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>Which strategy produced a resolution match.</summary>
public enum ResolutionMethod {
	/// <summary>No stored entity matched — the occurrence names something new.</summary>
	None,

	/// <summary>Normalized name or alias equality.</summary>
	Exact,

	/// <summary>Token-sort edit-distance similarity.</summary>
	Fuzzy,

	/// <summary>Embedding cosine similarity.</summary>
	Semantic,
}

/// <summary>
/// What resolution is asked about: one extracted occurrence, optionally carrying the embedding
/// of its surface form. The embedding is the caller's to provide — the projector batch-embeds
/// whole extraction runs, and a per-probe model call here would undo that batching.
/// </summary>
public sealed record EntityProbe(string Name, string EntityType, float[]? Embedding = null) {
	public string NormalizedName { get; } = EntityName.Normalize(Name);

	public static EntityProbe From(ExtractedEntity entity, float[]? embedding = null) =>
		new(entity.Name, entity.Type, embedding);
}

/// <summary>One batch-local candidate: the pending row beside the embedding it was minted with.</summary>
public sealed record PendingEntity(EntityRow Row, float[] Embedding);

/// <summary>
/// What one resolution decides over: the probe, and the batch-local pool of entities earlier
/// groups created or touched in the same batch. Resolution must see both candidate pools —
/// the store AND the uncommitted batch — or "Jon Smith" and "John Smith" arriving together
/// both miss and become two UNRELATED entities, neither merged nor linked. An empty pool
/// resolves against the store alone.
/// </summary>
public sealed record ResolutionProbe(EntityProbe Probe, IReadOnlyList<PendingEntity> Pending) {
	public static ResolutionProbe Of(EntityProbe probe) => new(probe, []);

	/// <summary>The pool scoped to the probe's type — resolution is type-strict by construction.</summary>
	public IEnumerable<PendingEntity> PendingOfType() =>
		Pending.Where(pending => pending.Row.EntityType == Probe.EntityType);
}

/// <summary>
/// Resolution's verdict on one probe: the stored entity it IS (when one matched), the score
/// that backed the match, and which strategy found it. <see cref="Match"/> null means the
/// probe names an entity the store has never seen.
/// </summary>
public sealed record EntityResolution {
	public static EntityResolution Unmatched { get; } = new();

	public EntityRow?       Match  { get; init; }
	public double           Score  { get; init; }
	public ResolutionMethod Method { get; init; } = ResolutionMethod.None;

	public bool IsMatch => Match is not null;
}

/// <summary>
/// One resolution strategy: probe in, verdict out. Each strategy scores BOTH candidate pools —
/// the store and the batch-local pending entities — and returns its best. Implementations are
/// type-strict by construction — every candidate lookup is scoped to the probe's entity type,
/// so a PERSON can never resolve to a LOCATION however similar the names.
/// </summary>
public interface IEntityResolver {
	ValueTask<EntityResolution> ResolveAsync(ResolutionProbe probe, CancellationToken ct = default);
}

public static class EntityResolverExtensions {
	/// <summary>Resolves against the store alone — the shape callers outside a projection batch use.</summary>
	public static ValueTask<EntityResolution> ResolveAsync(this IEntityResolver resolver, EntityProbe probe, CancellationToken ct = default) =>
		resolver.ResolveAsync(ResolutionProbe.Of(probe), ct);
}
