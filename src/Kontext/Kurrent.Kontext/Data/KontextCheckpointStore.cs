// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using Kurrent.Surge;

namespace Kurrent.Kontext.Data;

/// <summary>
/// The projections' checkpoint table — one row per projection key. The connection supplied per
/// call decides the catalog: on an engine-catalog connection the table is native; on a
/// lance-redirected writer connection it lands in the lance catalog — REQUIRED whenever the
/// checkpoint must share a transaction with lance writes, because a transaction that writes
/// lance cannot touch any other attached database. The table carries no constraints (lance
/// CREATE TABLE rejects them), so the upsert is a MERGE keyed on the row itself.
///
/// Stores are monotonic: a position only ever advances, so a replayed batch writing an older
/// position is a no-op rather than an error.
/// </summary>
public sealed class KontextCheckpointStore(string key) {
    public string Key { get; } = key;

    /// <summary>Creates the checkpoint table. Idempotent — safe on every start.</summary>
    public void EnsureSchema(DuckDBAdvancedConnection connection) =>
        connection.ExecuteNonQuery<CreateCheckpointsTable>();

    /// <summary>The position to resume from — <see cref="RecordPosition.Unset"/> until the first store.</summary>
    public RecordPosition Load(DuckDBAdvancedConnection connection) =>
        connection.QueryFirstOrDefault<CheckpointKeyArgs, CheckpointRow, GetCheckpoint>(new(Key)).TryGet(out var row) && row.Position is { } position
            ? RecordPosition.ForLog((ulong)position)
            : RecordPosition.Unset;

    /// <summary>
    /// Advances this key's position. Monotonic by construction: an older or equal position —
    /// a replayed batch — changes nothing.
    /// </summary>
    public void Store(DuckDBAdvancedConnection connection, RecordPosition position) {
        if ((ulong?)position is not { } value)
            return;

        connection.ExecuteNonQuery<StoreCheckpointArgs, StoreCheckpoint>(new(Key, (long)value));
    }
}

file readonly record struct CheckpointKeyArgs(string Key);

file readonly record struct StoreCheckpointArgs(string Key, long Position);

file readonly record struct CheckpointRow(long? Position);

// No PRIMARY KEY on purpose: lance CREATE TABLE rejects constraints outright, and the store
// must work in both catalogs. The MERGE below is keyed on the row, so nothing needs one.
// timestamp is Unix epoch milliseconds.
file struct CreateCheckpointsTable : IParameterlessStatement {
    public static ReadOnlySpan<byte> CommandText =>
        """
        CREATE TABLE IF NOT EXISTS checkpoints (
            key        VARCHAR,
            position   BIGINT,
            timestamp  BIGINT
        )
        """u8;
}

file struct GetCheckpoint : IQuery<CheckpointKeyArgs, CheckpointRow> {
    public static StatementBindingResult Bind(in CheckpointKeyArgs args, PreparedStatement statement) =>
        new(statement) {
            args.Key,
        };

    public static ReadOnlySpan<byte> CommandText =>
        """
        SELECT position
        FROM checkpoints
        WHERE key = $1
        LIMIT 1
        """u8;

    public static CheckpointRow Parse(ref DataChunk.Row row) => new(row.TryReadInt64());
}

// Insert-or-advance in one statement: the monotonic guard lives in the MATCHED arm's
// condition, the same facet-guarded MERGE shape the memories writer uses on lance.
file struct StoreCheckpoint : IPreparedStatement<StoreCheckpointArgs> {
    public static StatementBindingResult Bind(in StoreCheckpointArgs args, PreparedStatement statement) =>
        new(statement) {
            args.Key,
            args.Position,
        };

    public static ReadOnlySpan<byte> CommandText =>
        """
        MERGE INTO checkpoints AS t
        USING (SELECT $1 AS key, $2 AS position) AS s
        ON t.key = s.key
        WHEN NOT MATCHED THEN INSERT (key, position, timestamp)
            VALUES (s.key, s.position, epoch_ms(now()))
        WHEN MATCHED AND (t.position IS NULL OR t.position < s.position) THEN UPDATE SET
            position  = s.position,
            timestamp = epoch_ms(now())
        """u8;
}
