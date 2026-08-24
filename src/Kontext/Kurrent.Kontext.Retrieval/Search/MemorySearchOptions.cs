// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// One search result: the stored memory plus the scores of whichever search legs ran.
/// - HybridScore: the alpha blend hybrid results are ordered by — larger = better, rank-shaped, never comparable across queries; only hybrid produces it, null in vector and full-text modes
/// - VectorDistance: the vector leg — smaller = closer (squared L2 under the default metric); null = the vector leg did not surface the row
/// - KeywordScore: the BM25 keyword leg — larger = better, corpus-relative; null = the keyword leg did not surface the row
///
/// No field ever lies: a score a mode does not produce is null, never a fabricated number.
/// </summary>
public readonly record struct MemoryHit(MemoryContracts.StoredMemory Memory, double? HybridScore, double? VectorDistance, double? KeywordScore);

/// <summary>
/// The Lance vector-search knobs plus the result limit. Defaults are the validated operational
/// settings from the knowledge base. A mutable settings class by design — config binding does not
/// cope with records.
/// </summary>
public sealed class VectorSearchOptions {
    /// <summary>
    /// The page size: the engine returns exactly this many rows.
    /// <para>Tag filters do NOT inflate it: non-empty containment pushes down as a true prefilter,
    /// so ranking only ever sees matching rows. Verified by TagPrefilterPushdownProbeTests — a
    /// 10-row tagged minority buried under 990 better matches still returns a full page at the
    /// default K, which is only possible if the filter ran before ranking.</para>
    /// </summary>
    public int K { get; set; } = 10;

    /// <summary>Re-ranks the top candidates with exact distances — mandatory with PQ-family vector indexes.</summary>
    public int RefineFactor { get; set; } = 4;

    /// <summary>True: filter before ranking (k matching rows); false: filter after ranking (may return fewer).</summary>
    public bool Prefilter { get; set; } = true;

    /// <summary>
    /// How many IVF partitions the search probes. Null = the engine's default.
    /// Irrelevant while indexes use num_partitions = 1; a recall/latency dial once they shard.
    /// </summary>
    public long? Nprobs { get; set; }

    /// <summary>
    /// False forces an exact brute-force scan even when a vector index exists — the exactness
    /// escape hatch (debugging recall, parity checks). Null = the engine's default.
    /// </summary>
    public bool? UseIndex { get; set; }
}

/// <summary>
/// The Lance full-text (BM25) search knobs plus the result limit — genuinely everything
/// <c>lance_fts</c> accepts. Defaults are the validated operational settings from the knowledge
/// base. A mutable settings class by design — config binding does not cope with records.
/// </summary>
public sealed class FullTextSearchOptions {
    /// <summary>
    /// The page size: the engine returns exactly this many rows.
    /// <para>Tag filters do NOT inflate it: non-empty containment pushes down as a true prefilter,
    /// so ranking only ever sees matching rows. See TagPrefilterPushdownProbeTests.</para>
    /// </summary>
    public int K { get; set; } = 10;

    /// <summary>True: filter before ranking (k matching rows); false: filter after ranking (may return fewer).</summary>
    public bool Prefilter { get; set; } = true;
}

/// <summary>
/// The Lance hybrid-search knobs plus the result limit. Defaults are the validated operational
/// settings from the knowledge base. A mutable settings class by design — config binding does not
/// cope with records.
/// </summary>
public sealed class HybridSearchOptions {
    /// <summary>
    /// The page size per search leg: the engine returns exactly this many rows.
    /// <para>Tag filters do NOT inflate it: non-empty containment pushes down as a true prefilter,
    /// so ranking only ever sees matching rows — a tag-scoped search is measurably FASTER than an
    /// unscoped one on the same corpus (5.0ms vs 6.5ms over 1000 rows), because the filter shrinks
    /// the work rather than adding a pass. See TagPrefilterPushdownProbeTests.</para>
    /// </summary>
    public int K { get; set; } = 10;

    /// <summary>Blend of the two legs: 0 = pure keyword, 1 = pure vector.</summary>
    public double Alpha { get; set; } = 0.5;

    /// <summary>Re-ranks the top candidates with exact distances — mandatory with PQ-family vector indexes.</summary>
    public int RefineFactor { get; set; } = 4;

    /// <summary>How many candidates each modality fetches (k × factor) before the hybrid merge.</summary>
    public int OversampleFactor { get; set; } = 4;

    /// <summary>True: filter before ranking (k matching rows); false: filter after ranking (may return fewer).</summary>
    public bool Prefilter { get; set; } = true;

    /// <summary>
    /// How many IVF partitions the vector leg probes. Null = the engine's default.
    /// Irrelevant while indexes use num_partitions = 1; a recall/latency dial once they shard.
    /// </summary>
    public long? Nprobs { get; set; }

    /// <summary>
    /// False forces an exact brute-force scan even when a vector index exists — the exactness
    /// escape hatch (debugging recall, parity checks). Null = the engine's default.
    /// </summary>
    public bool? UseIndex { get; set; }
}
