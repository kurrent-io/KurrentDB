// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.RegularExpressions;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal static class CustomIndexSql {
	private static readonly Regex TableNameRegex = new("^[a-z][a-z0-9_-]*$", RegexOptions.Compiled);
	public static string GetTableNameFor(string indexName) {
		var tableName = $"idx_custom__{indexName}";

		// we validate the table name for safety reasons although DuckDB allows a large set of characters when using quoted identifiers
		if (!TableNameRegex.IsMatch(tableName))
			throw new Exception($"Invalid table name: {tableName}");

		return tableName;
	}

	public static string GenerateInFlightTableNameFor(string indexName) {
		return $"inflight_idx_custom__{indexName}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
	}

	public static void DeleteCustomIndex(DuckDBAdvancedConnection connection, string indexName) {
		connection.ExecuteNonQuery<DeleteCheckpointNonQueryArgs, DeleteCheckpointNonQuery>(new DeleteCheckpointNonQueryArgs(indexName));

		var tableName = GetTableNameFor(indexName);
		var query = new DeleteCustomIndexNonQuery(tableName);
		connection.ExecuteNonQuery(ref query);
	}
}

internal class CustomIndexSql<TField>(string indexName) where TField : IField {
	public string TableName { get; } = CustomIndexSql.GetTableNameFor(indexName);
	public ReadOnlyMemory<byte> TableNameUtf8 { get; } = Encoding.UTF8.GetBytes(CustomIndexSql.GetTableNameFor(indexName));

	public void CreateCustomIndex(DuckDBAdvancedConnection connection) {
		var query = new CreateCustomIndexNonQuery(TableName, TField.GetCreateStatement());
		connection.ExecuteNonQuery(ref query);
	}
	public List<IndexQueryRecord> ReadCustomIndexForwardsQuery(DuckDBConnectionPool db, ReadCustomIndexQueryArgs args) {
		var query = new ReadCustomIndexForwardsQuery(TableName, args.ExcludeFirst, args.Partition.GetQueryStatement());
		using (db.Rent(out var connection))
			return connection.ExecuteQuery<ReadCustomIndexQueryArgs, IndexQueryRecord, ReadCustomIndexForwardsQuery>(ref query, args).ToList();
	}

	public List<IndexQueryRecord> ReadCustomIndexBackwardsQuery(DuckDBConnectionPool db, ReadCustomIndexQueryArgs args) {
		var query = new ReadCustomIndexBackwardsQuery(TableName, args.ExcludeFirst, args.Partition.GetQueryStatement());
		using (db.Rent(out var connection))
			return connection.ExecuteQuery<ReadCustomIndexQueryArgs, IndexQueryRecord, ReadCustomIndexBackwardsQuery>(ref query, args).ToList();
	}

	public GetCheckpointResult? GetCheckpoint(DuckDBAdvancedConnection connection, GetCheckpointQueryArgs args) {
		return connection.QueryFirstOrDefault<GetCheckpointQueryArgs, GetCheckpointResult, GetCheckpointQuery>(args);
	}

	public void SetCheckpoint(DuckDBAdvancedConnection connection, SetCheckpointQueryArgs args) {
		connection.ExecuteNonQuery<SetCheckpointQueryArgs, SetCheckpointNonQuery>(args);
	}

	public GetLastIndexedRecordResult? GetLastIndexedRecord(DuckDBAdvancedConnection connection) {
		var query = new GetLastIndexedRecordQuery(TableName);
		return connection.ExecuteQuery<GetLastIndexedRecordResult, GetLastIndexedRecordQuery>(ref query).FirstOrDefault();
	}
}

file readonly record struct CreateCustomIndexNonQuery(string TableName, string CreatePartitionStatement) : IDynamicParameterlessStatement {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		create table if not exists "{0}"
		(
			log_position bigint not null,
			commit_position bigint null,
			event_number bigint not null,
			created bigint not null
			{1}
		)
		"""
		);

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = CreatePartitionStatement;
	}
}

file readonly record struct DeleteCustomIndexNonQuery(string TableName) : IDynamicParameterlessStatement {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse("drop table if exists \"{0}\"");
	public void FormatCommandTemplate(Span<object?> args) => args[0] = TableName;
}

internal record struct ReadCustomIndexQueryArgs(long StartPosition, long EndPosition, bool ExcludeFirst, int Count, IField Partition);

file readonly record struct ReadCustomIndexForwardsQuery(string TableName, bool ExcludeFirst, string PartitionQuery) : IDynamicQuery<ReadCustomIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse("select log_position, commit_position, event_number from \"{0}\" where log_position >{1} ? and log_position < ? {2} order by rowid limit ?");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = PartitionQuery;
	}

	public static BindingContext Bind(in ReadCustomIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		statement.Bind(index++, args.EndPosition);
		args.Partition.BindTo(statement, ref index);
		statement.Bind(index, args.Count);

		return new BindingContext(statement, completed: true);
	}

	public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
}

file readonly record struct ReadCustomIndexBackwardsQuery(string TableName, bool ExcludeFirst, string PartitionQuery) : IDynamicQuery<ReadCustomIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse("select log_position, commit_position, event_number from \"{0}\" where log_position <{1} ? {2} order by rowid desc limit ?");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = PartitionQuery;
	}

	public static BindingContext Bind(in ReadCustomIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		args.Partition.BindTo(statement, ref index);
		statement.Bind(index, args.Count);

		return new BindingContext(statement, completed: true);
	}

	public static IndexQueryRecord Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
}

internal record struct GetCheckpointQueryArgs(string IndexName);
internal record struct GetCheckpointResult(long PreparePosition, long? CommitPosition, long Timestamp);
file struct GetCheckpointQuery : IQuery<GetCheckpointQueryArgs, GetCheckpointResult> {
	public static BindingContext Bind(in GetCheckpointQueryArgs args, PreparedStatement statement) => new(statement) { args.IndexName };
	public static ReadOnlySpan<byte> CommandText => "select log_position, commit_position, created from idx_custom_checkpoints where index_name = ? limit 1"u8;
	public static GetCheckpointResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
}

internal record struct SetCheckpointQueryArgs(string IndexName, long PreparePosition, long? CommitPosition, long Created);
file struct SetCheckpointNonQuery : IPreparedStatement<SetCheckpointQueryArgs> {
	public static BindingContext Bind(in SetCheckpointQueryArgs args, PreparedStatement statement) => new(statement) { args.IndexName, args.PreparePosition, args.CommitPosition, args.Created };
	public static ReadOnlySpan<byte> CommandText => "insert or replace into idx_custom_checkpoints (index_name,log_position,commit_position,created) VALUES ($1,$2,$3,$4)"u8;
}

internal record struct DeleteCheckpointNonQueryArgs(string IndexName);
file struct DeleteCheckpointNonQuery : IPreparedStatement<DeleteCheckpointNonQueryArgs> {
	public static BindingContext Bind(in DeleteCheckpointNonQueryArgs args, PreparedStatement statement) => new(statement) { args.IndexName };
	public static ReadOnlySpan<byte> CommandText => "delete from idx_custom_checkpoints where index_name = ?"u8;
}

internal record struct GetLastIndexedRecordResult(long PreparePosition, long? CommitPosition, long Timestamp);
file readonly record struct GetLastIndexedRecordQuery(string TableName) : IDynamicQuery<GetLastIndexedRecordResult> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse("select log_position, commit_position, created from \"{0}\" order by rowid desc limit 1");
	public void FormatCommandTemplate(Span<object?> args) => args[0] = TableName;
	public static GetLastIndexedRecordResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
}
