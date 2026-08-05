// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using Kurrent.Surge;

namespace Kurrent.Kontext.Data;

/// <summary>
/// The projections' checkpoint table — one row per projection key, in the native engine catalog.
/// The connection is supplied per call: the caller decides which connection — and therefore
/// which transaction — a checkpoint write rides. Store the checkpoint strictly after the data
/// it claims: lance commits per statement, so the position can lag the data and replay a
/// batch, never lead it and skip one.
///
/// Stores are monotonic: a position only ever advances, so a replayed batch writing an older
/// position is a no-op rather than an error.
/// </summary>
public sealed class KontextCheckpointStore(string key) {
    public string Key { get; } = key;

    /// <summary>Creates the checkpoint table and this key's row. Idempotent — safe on every start.</summary>
    public void EnsureSchema(DuckDBAdvancedConnection connection) {
        // A prepared statement takes exactly one statement — the DDL and the row cannot batch.
        connection.ExecuteNonQuery<CreateCheckpointsTable>();
        connection.ExecuteNonQuery<CheckpointKeyArgs, UpsertCheckpointRow>(new(Key));
    }

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

// timestamp is Unix epoch milliseconds, NULL until the first store.
file struct CreateCheckpointsTable : IParameterlessStatement {
    public static ReadOnlySpan<byte> CommandText =>
        """
        CREATE TABLE IF NOT EXISTS checkpoints (
            key        VARCHAR PRIMARY KEY,
            position   BIGINT  DEFAULT NULL,
            timestamp  BIGINT  DEFAULT NULL
        )
        """u8;
}

file struct UpsertCheckpointRow : IPreparedStatement<CheckpointKeyArgs> {
    public static StatementBindingResult Bind(in CheckpointKeyArgs args, PreparedStatement statement) =>
        new(statement) {
            args.Key,
        };

    public static ReadOnlySpan<byte> CommandText =>
        """
        INSERT INTO checkpoints (key) VALUES ($1)
        ON CONFLICT (key) DO NOTHING
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

file struct StoreCheckpoint : IPreparedStatement<StoreCheckpointArgs> {
    public static StatementBindingResult Bind(in StoreCheckpointArgs args, PreparedStatement statement) =>
        new(statement) {
            args.Key,
            args.Position,
        };

    public static ReadOnlySpan<byte> CommandText =>
        """
        UPDATE checkpoints
        SET position  = $2,
            timestamp = epoch_ms(now())
        WHERE key = $1
          AND (position IS NULL OR position < $2)
        """u8;
}
