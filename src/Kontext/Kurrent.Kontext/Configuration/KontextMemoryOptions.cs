// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Configuration;

/// <summary>
/// Host-facing configuration for the memory service. A mutable settings class by design — config
/// binding does not cope with records.
/// </summary>
public sealed class KontextMemoryOptions {
    /// <summary>
    /// The most neighbours retain will report per stored memory, whatever the caller asks for.
    /// <para>Five by default. Measured: the search costs the same at 3, 5, 10 and 20 (4.3-4.5ms
    /// over 1000 rows), so this is a context budget rather than a performance knob — each neighbour
    /// rides back as a LeanMemory the caller has to read.</para>
    /// </summary>
    public int MaxNeighbours { get; set; } = 5;

    /// <summary>
    /// The neighbour search's blend of the two engine legs: 0 = pure keyword, 1 = pure vector.
    /// <para>0.45 matches the recall chain, and the sweep in RelatedPipelineTuningProbeTests shows
    /// duplicate-detection MRR plateaus from 0.45 to 1.0 — while pure keyword (0.0) scores ZERO on
    /// reworded duplicates.</para>
    /// <para>It orders the pool only. Each neighbour reports its raw per-leg distance, because the
    /// engine's blended score is normalised across the returned pool and means nothing outside it.</para>
    /// </summary>
    public double NeighbourAlpha { get; set; } = 0.45;
}
