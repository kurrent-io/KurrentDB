// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// The migration history — one entry per executed migration: which key ran, when, and how long it
/// took. Append-only, like the stream it records. The adapter owns the storage and the timestamps,
/// nothing else — ordering and uniqueness are the stream's own invariants, validated by the engine
/// before anything runs.
/// </summary>
public interface IMigrationJournal {
    /// <summary>
    /// Creates the history storage when missing. Runs every boot, before the history load.
    /// </summary>
    ValueTask EnsureAsync(CancellationToken ct = default);

    /// <summary>
    /// Records an executed migration.
    /// </summary>
    ValueTask RecordAsync(ExecutedMigration entry, CancellationToken ct = default);

    /// <summary>
    /// The whole history, ordered by version. The engine reads it once per boot: the highest
    /// version is the store's state, and every recorded key is checked against the key the code
    /// now declares for that version.
    /// </summary>
    ValueTask<IReadOnlyList<ExecutedMigration>> ListAsync(CancellationToken ct = default);
}
