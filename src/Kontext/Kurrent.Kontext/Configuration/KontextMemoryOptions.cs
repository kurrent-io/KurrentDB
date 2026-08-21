// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Configuration;

/// <summary>
/// Host-facing configuration for the memory service. A mutable settings class by design — config
/// binding does not cope with records.
/// </summary>
public sealed class KontextMemoryOptions {
    /// <summary>
    /// How many candidates a DEFERRED result reports.
    /// <para>Five by default. Measured: the search costs the same at 3, 5, 10 and 20 (4.3-4.5ms
    /// over 1000 rows), so this is a context budget rather than a performance knob — each candidate
    /// rides back as a LeanMemory the caller has to read.</para>
    /// </summary>
    public int RelatedLimit { get; set; } = 5;

    /// <summary>
    /// The duplicate search's blend of the two engine legs: 0 = pure keyword, 1 = pure vector.
    /// <para>0.45 matches the recall chain, and the sweep in RelatedPipelineTuningProbeTests shows
    /// duplicate-detection MRR plateaus from 0.45 to 1.0 — while pure keyword (0.0) scores ZERO on
    /// reworded duplicates.</para>
    /// <para>It selects the candidate pool only. The MERGE/APPEND call reads the raw per-leg
    /// distance, never the engine's blended score.</para>
    /// </summary>
    public double RelatedAlpha { get; set; } = 0.45;

    /// <summary>
    /// Below this vector distance retain MERGES: the incoming memory supersedes the neighbour.
    /// <para>Squared L2 over unit-length embeddings, so the number is comparable across queries.
    /// PROVISIONAL — DuplicateDistanceSeparationProbeTests measured 12 planted pairs against 300
    /// LoCoMo turns, where every lexical restatement fell under 0.5871 and the closest stranger sat
    /// at 1.2230. Calibrate on LoCoMo/LongMemEval before trusting it at corpus scale.</para>
    /// </summary>
    public double MergeCeiling { get; set; } = 1.2;

    /// <summary>
    /// Above this vector distance retain APPENDS a distinct memory. Between the two thresholds it
    /// DEFERS and lets the caller decide.
    /// <para>PROVISIONAL, on the same measurement: reworded duplicates reached 1.7604 while
    /// strangers started at 1.2230, so the two populations genuinely overlap and no single
    /// threshold can split them.</para>
    /// </summary>
    public double AppendFloor { get; set; } = 1.76;
}
