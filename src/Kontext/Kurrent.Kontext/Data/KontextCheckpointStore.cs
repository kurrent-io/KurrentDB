// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Surge;

namespace Kurrent.Kontext.Data;

public sealed class KontextCheckpointStore(string key) {
    public string Key { get; } = key;

    public void EnsureSchema(DuckDBAdvancedConnection connection) =>
        connection.ExecuteNonQuery<CreateCheckpointsTable>();

    public RecordPosition Load(DuckDBAdvancedConnection connection) =>
        connection.QueryFirstOrDefault<GetCheckpointArgs, Checkpoint, GetCheckpoint>(new(Key))
            .TryGet(out var row) && row.Position is { } position
            ? RecordPosition.ForLog((ulong)position)
            : RecordPosition.Unset;
    
    public void Store(DuckDBAdvancedConnection connection, LogPosition position) {
        if (position is { CommitPosition: { } commitPosition })
            connection.ExecuteNonQuery<StoreCheckpointArgs, StoreCheckpoint>(new(Key, (long)commitPosition));
    }
}

#region >> Quack Statements <<

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

file readonly record struct GetCheckpointArgs(string Key);

file readonly record struct Checkpoint(long? Position, long? Timestamp);

file struct GetCheckpoint : IQuery<GetCheckpointArgs, Checkpoint> {
    public static StatementBindingResult Bind(in GetCheckpointArgs args, PreparedStatement statement) =>
        new(statement) { args.Key, };

    public static ReadOnlySpan<byte> CommandText =>
        """
        SELECT 
            position,
            timestamp
        FROM checkpoints
        WHERE key = $1
        LIMIT 1
        """u8;

    public static Checkpoint Parse(ref DataChunk.Row row) => new(row.TryReadInt64(), row.TryReadInt64());
}

file readonly record struct StoreCheckpointArgs(string Key, long Position);

file struct StoreCheckpoint : IPreparedStatement<StoreCheckpointArgs> {
    public static StatementBindingResult Bind(in StoreCheckpointArgs args, PreparedStatement statement) =>
        new(statement) { args.Key, args.Position, };

    public static ReadOnlySpan<byte> CommandText =>
        """
        MERGE INTO checkpoints AS t
        USING (SELECT $1 AS key, $2 AS position) AS s
        ON t.key = s.key
        WHEN NOT MATCHED THEN
            INSERT (key, position, timestamp)
            VALUES (s.key, s.position, epoch_ms(now()))
        WHEN MATCHED AND (t.position IS NULL OR t.position < s.position) THEN
            UPDATE SET
                position  = s.position,
                timestamp = epoch_ms(now())
        """u8;
}

#endregion