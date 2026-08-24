// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Contracts.V3.Entities;

namespace Kurrent.Kontext.Entities;

/// <summary>Entity a name resolved to, with method and confidence.</summary>
public sealed record ResolvedEntity(string EntityId, double Confidence, ResolutionMethod Method);

/// <summary>A name to resolve semantically, with its embedding.</summary>
public sealed record SemanticQuery(EntityKey Key, float[] Embedding);

/// <summary>Best semantic match for a name, with the runners-up in <see cref="Candidates"/> for later tiers.</summary>
public sealed record SemanticMatch(
    string EntityId,
    double Confidence,
    bool Corroborated = false,
    IReadOnlyList<EntityCandidate>? Candidates = null
);

public sealed class EntityResolverOptions {
    /// <summary>Runs the lexical tier between exact and semantic matching.</summary>
    public bool LexicalTier { get; set; } = true;

    /// <summary>Lets a matching spelling lower the semantic merge bar.</summary>
    public bool CorroboratedMerging { get; set; } = true;

    /// <summary>Lets a model decide names no other tier would merge, skipped when no disambiguator is given.</summary>
    public bool LlmTier { get; set; } = true;

    /// <summary>Similarity above which a semantic match merges.</summary>
    public double SemanticMergeThreshold { get; set; } = 0.97;

    /// <summary>The old cascade, kept for benchmarking.</summary>
    public static EntityResolverOptions Legacy =>
        new() { LexicalTier = false, CorroboratedMerging = false, SemanticMergeThreshold = 0.95 };
}
