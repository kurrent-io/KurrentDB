// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Configuration;

/// <summary>
/// Host-facing configuration for the memory service. A mutable settings class by design — config
/// binding does not cope with records.
/// </summary>
public sealed class KontextMemoryOptions {
    /// <summary>
    /// How many neighbours retain reports per stored memory. The contract promises every retain
    /// reports its nearest LIVE memories "capped by configuration"; this is that cap.
    /// <para>Five by default. Measured: the neighbour search costs the same at 3, 5, 10 and 20
    /// (4.3-4.5ms over 1000 rows), so this is a context budget rather than a performance knob —
    /// each neighbour rides back as a LeanMemory the caller has to read.</para>
    /// </summary>
    public int RelatedLimit { get; set; } = 5;

    /// <summary>
    /// The neighbour search's blend of the two engine legs: 0 = pure keyword, 1 = pure vector.
    /// <para>0.45 matches the recall chain, and the sweep in RelatedPipelineTuningProbeTests shows
    /// duplicate-detection MRR plateaus from 0.45 to 1.0 — while pure keyword (0.0) scores ZERO on
    /// reworded duplicates, which is the case `related` exists to catch.</para>
    /// </summary>
    public double RelatedAlpha { get; set; } = 0.45;
}
