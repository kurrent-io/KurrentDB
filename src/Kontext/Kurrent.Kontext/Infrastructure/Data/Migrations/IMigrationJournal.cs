// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// The migration history — one entry per executed step: what ran, when, and how long it took.
/// Append-only, like the stream it records; the highest version present is the store's state.
/// The adapter owns the storage and the timestamps, nothing else — ordering and uniqueness
/// are the stream's own invariants, validated by the engine before anything runs.
/// </summary>
public interface IMigrationJournal {
    /// <summary>Creates the history storage when missing. Runs every boot, before the version load.</summary>
    ValueTask EnsureAsync(CancellationToken ct = default);

    /// <summary>The highest executed version — 0 when nothing ever ran, so a fresh store and an
    /// empty history are the same case: everything is pending.</summary>
    ValueTask<int> LoadCurrentVersionAsync(CancellationToken ct = default);

    ValueTask RecordAsync(ExecutedMigration entry, CancellationToken ct = default);
}
