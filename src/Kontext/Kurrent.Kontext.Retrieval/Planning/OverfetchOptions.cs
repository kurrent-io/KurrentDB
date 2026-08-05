// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// How far past the requested limit the searches over-fetch. Fusion needs slack: a memory ranked
/// 15th by one leg and 3rd by another must be in both pools to fuse well, and post-hoc cuts
/// (min-score, dedup, reorder) eat candidates. References converge on limit×2…4; the LoCoMo
/// ranking corpus measured a 30-candidate pool ahead of 60 at limit 10 (deeper pools stretch the
/// relevance min-max over more tail and feed MMR more distractors), so the defaults land at the
/// low end: ×3 with a floor of 30.
/// </summary>
public sealed class OverfetchOptions {
    public int Factor { get; set; } = 3;

    public int Floor { get; set; } = 30;

    public int PoolSizeFor(int limit) => Math.Max(limit * Factor, Floor);
}
