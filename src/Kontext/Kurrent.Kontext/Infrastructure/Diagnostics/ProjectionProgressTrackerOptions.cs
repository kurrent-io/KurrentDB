// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Diagnostics;

/// <summary>
/// Naming and sourcing for one <see cref="ProjectionProgressTracker"/>: where its metrics live
/// (<c>{service}.{Scope}.gap|lag|commit.seconds</c>), how samples are tagged, and the
/// authoritative head of the source being trailed.
/// </summary>
public class ProjectionProgressTrackerOptions {
    public required string Scope  { get; set; }
    public required string TagKey { get; set; }
    public required string Name   { get; set; }

    /// <summary>
    /// The latest available mark of the source. Always injected so one upstream source
    /// can serve many trackers without each duplicating head tracking.
    /// </summary>
    public required Func<ProgressMark> GetHead { get; set; }

    public string GapUnit { get; set; } = "bytes";

    public double[] CommitSecondsBuckets { get; set; } = [
        0.000_001, // 1 microsecond
        0.000_01, 0.000_1, 0.001, // 1 millisecond
        0.01, 0.1, 1, // 1 second
        10,
    ];
}
