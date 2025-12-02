// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.RegularExpressions;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal static class CustomIndexSql {
	private static readonly Regex TableNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
	public static ReadOnlyMemory<byte> GetTableNameFor(string indexName) {
		var tableName = $"idx_custom__{indexName}";

		if (!TableNameRegex.IsMatch(tableName))
			throw new Exception($"Invalid DuckDB table name: {tableName}");

		return Encoding.UTF8.GetBytes(tableName);
	}

	public static string GenerateInFlightTableNameFor(string indexName) {
		return $"inflight_idx_custom__{indexName}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
	}

	public static void DeleteCustomIndex(DuckDBAdvancedConnection connection, string indexName) {
		Span<byte> buffer = stackalloc byte[512];
		var tableName = GetTableNameFor(indexName);
		var cmd = new SqlCommandBuilder(buffer) { "drop table if exists "u8, tableName.Span }.Command;
		connection.ExecuteAdHocNonQuery(cmd);
	}
}

internal class CustomIndexSql<TPartitionKey>(string indexName) where TPartitionKey : ITPartitionKey {
	private const int MaxSqlCommandLength = 512;
	public ReadOnlyMemory<byte> TableName { get; } = CustomIndexSql.GetTableNameFor(indexName);

	/// <summary>
	/// Create a custom index
	/// </summary>
	public void CreateCustomIndex(DuckDBAdvancedConnection connection) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"create table if not exists "u8, TableName.Span,
			"""
			 (
			 	log_position bigint not null,
			 	commit_position bigint null,
			 	event_number bigint not null,
			 	created bigint not null
			"""u8,
			TPartitionKey.GetCreateStatement(),
			");"u8
		}.Command;

		connection.ExecuteAdHocNonQuery(cmd);
	}

	/// <summary>
	/// Delete a custom index
	/// </summary>
	public void DeleteCustomIndex(DuckDBAdvancedConnection connection) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"drop table if exists "u8, TableName.Span
		}.Command;
		connection.ExecuteAdHocNonQuery(cmd);
	}

	/// <summary>
	/// Get index records for the custom index with a log position greater than the start position
	/// </summary>
	public IEnumerable<IndexQueryRecord> ReadCustomIndexQueryExcl(DuckDBAdvancedConnection connection, ITPartitionKey partitionKey, ReadCustomIndexQueryArgs args) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"select log_position, commit_position, event_number from "u8,
			TableName.Span,
			" where log_position > ? and log_position < ? "u8,
			partitionKey.GetQueryStatement(),
			" order by rowid limit ?"u8
		}.Command;
		return GetIndexRecordsForwards(connection, cmd, partitionKey, args);
	}


	/// <summary>
	/// Get index records for the custom index with a log position greater or equal than the start position
	/// </summary>
	public IEnumerable<IndexQueryRecord> ReadCustomIndexQueryIncl(DuckDBAdvancedConnection connection, ITPartitionKey partitionKey, ReadCustomIndexQueryArgs args) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"select log_position, commit_position, event_number from "u8,
			TableName.Span,
			" where log_position >= ? and log_position < ? "u8,
			partitionKey.GetQueryStatement(),
			" order by rowid limit ?"u8
		}.Command;
		return GetIndexRecordsForwards(connection, cmd, partitionKey, args);
	}

	/// <summary>
	/// Get index records for the custom index with the log position less than the start position
	/// </summary>
	public IEnumerable<IndexQueryRecord> ReadCustomIndexBackQueryExcl(DuckDBAdvancedConnection connection, ITPartitionKey partitionKey, ReadCustomIndexQueryArgs args) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"select log_position, commit_position, event_number from "u8,
			TableName.Span,
			" where log_position < ? "u8,
			partitionKey.GetQueryStatement(),
			" order by rowid limit ?"u8
		}.Command;
		return GetIndexRecordsBackwards(connection, cmd, partitionKey, args);
	}

	/// <summary>
	/// Get index records for the custom index with log position less or equal than the start position
	/// </summary>
	public IEnumerable<IndexQueryRecord> ReadCustomIndexBackQueryIncl(DuckDBAdvancedConnection connection, ITPartitionKey partitionKey, ReadCustomIndexQueryArgs args) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"select log_position, commit_position, event_number from "u8,
			TableName.Span,
			" where log_position <= ? "u8,
			partitionKey.GetQueryStatement(),
			" order by rowid limit ?"u8
		}.Command;
		return GetIndexRecordsBackwards(connection, cmd, partitionKey, args);
	}

	/// <summary>
	/// Get the checkpoint
	/// </summary>
	public GetCheckpointResult? GetCheckpoint(DuckDBAdvancedConnection connection, GetCheckpointQueryArgs args) {
		return connection.QueryFirstOrDefault<GetCheckpointQueryArgs, GetCheckpointResult, GetCheckpointQuery>(args);
	}

	/// <summary>
	/// Set the checkpoint
	/// </summary>
	public void SetCheckpoint(DuckDBAdvancedConnection connection, SetCheckpointQueryArgs args) {
		connection.ExecuteNonQuery<SetCheckpointQueryArgs, SetCheckpointNonQuery>(args);
	}

	/// <summary>
	/// Get the last indexed record
	/// </summary>
	public LastIndexedRecordResult? GetLastIndexedRecord(DuckDBAdvancedConnection connection) {
		Span<byte> buffer = stackalloc byte[MaxSqlCommandLength];
		var cmd = new SqlCommandBuilder(buffer) {
			"select log_position, commit_position, created from "u8,
			TableName.Span,
			" order by rowid desc limit 1"u8,
		}.Command;

		using var result = connection.ExecuteAdHocQuery(cmd);
		if (result.TryFetch(out var chunk) && chunk.TryRead(out var row)) {
			return new LastIndexedRecordResult {
				PreparePosition = row.ReadInt64(),
				CommitPosition = row.TryReadInt64(),
				Timestamp = row.ReadInt64()
			};
		}

		return null;
	}

	private static IEnumerable<IndexQueryRecord> GetIndexRecordsForwards(DuckDBAdvancedConnection connection, ReadOnlySpan<byte> cmd, ITPartitionKey partitionKey, ReadCustomIndexQueryArgs args) {
		var statement = new PreparedStatement(connection, cmd);
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		statement.Bind(index++, args.EndPosition);
		partitionKey.BindTo(statement, ref index);
		statement.Bind(index++, args.Count);
		return GetIndexRecords(statement);
	}

	private static IEnumerable<IndexQueryRecord> GetIndexRecordsBackwards(DuckDBAdvancedConnection connection, ReadOnlySpan<byte> cmd, ITPartitionKey partitionKey, ReadCustomIndexQueryArgs args) {
		var statement = new PreparedStatement(connection, cmd);
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		partitionKey.BindTo(statement, ref index);
		statement.Bind(index++, args.Count);
		return GetIndexRecords(statement);
	}

	private static IEnumerable<IndexQueryRecord> GetIndexRecords(PreparedStatement preparedStatement) {
		try {
			using var result = preparedStatement.ExecuteQuery(useStreaming: false);
			while (result.TryFetch(out var chunk)) {
				while (chunk.TryRead(out var row)) {
					yield return new IndexQueryRecord(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
				}
			}
		} finally {
			preparedStatement.ClearBindings();
			preparedStatement.Dispose();
		}
	}
}

public record struct ReadCustomIndexQueryArgs(long StartPosition, long EndPosition, int Count);
public record struct LastIndexedRecordResult(long PreparePosition, long? CommitPosition, long Timestamp);

public record struct GetCheckpointQueryArgs(string IndexName);
public record struct GetCheckpointResult(long PreparePosition, long? CommitPosition, long Timestamp);
public struct GetCheckpointQuery : IQuery<GetCheckpointQueryArgs, GetCheckpointResult> {
	public static BindingContext Bind(in GetCheckpointQueryArgs args, PreparedStatement statement)
		=> new(statement) { args.IndexName };

	public static ReadOnlySpan<byte> CommandText => "select log_position, commit_position, created from idx_custom_checkpoints where index_name=$1 limit 1"u8;

	public static GetCheckpointResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
}

public record struct SetCheckpointQueryArgs(string IndexName, long PreparePosition, long? CommitPosition, long Created);
public struct SetCheckpointNonQuery : IPreparedStatement<SetCheckpointQueryArgs> {
	public static BindingContext Bind(in SetCheckpointQueryArgs args, PreparedStatement statement)
		=> new(statement) { args.IndexName, args.PreparePosition, args.CommitPosition, args.Created };

	public static ReadOnlySpan<byte> CommandText => "insert or replace into idx_custom_checkpoints (index_name,log_position,commit_position,created) VALUES ($1,$2,$3,$4)"u8;
}

