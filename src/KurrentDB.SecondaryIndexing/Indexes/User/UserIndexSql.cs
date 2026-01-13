// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.RegularExpressions;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal static partial class UserIndexSql {
	// we validate the table/column names for safety reasons, although DuckDB allows a large set of characters when using quoted identifiers
	private static readonly Regex IdentifierRegex = ValidationRegex();

	public static string GetTableNameFor(string indexName) {
		var tableName = $"idx_user__{indexName}";

		return IdentifierRegex.IsMatch(tableName)
			? tableName
			: throw new($"Invalid table name: {tableName}");
	}

	public static string GetColumnNameFor(string fieldName) {
		if (fieldName is "")
			return "";

		var columnName = $"field_{fieldName}";

		return IdentifierRegex.IsMatch(columnName)
			? columnName
			: throw new($"Invalid column name: {columnName}");
	}

	public static string GenerateInFlightTableNameFor(string indexName) {
		return $"inflight_idx_user__{indexName}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
	}

	public static void DeleteUserIndex(DuckDBAdvancedConnection connection, string indexName) {
		connection.ExecuteNonQuery<DeleteCheckpointNonQueryArgs, DeleteCheckpointNonQuery>(new(indexName));

		var tableName = GetTableNameFor(indexName);
		var query = new DeleteUserIndexNonQuery(tableName);
		connection.ExecuteNonQuery(ref query);
	}

	[GeneratedRegex("^[a-z][a-z0-9_-]*$", RegexOptions.Compiled)]
	private static partial Regex ValidationRegex();
}

internal class UserIndexSql<TField>(string indexName, string fieldName) where TField : IField {
	public string TableName { get; } = UserIndexSql.GetTableNameFor(indexName);
	public string FieldColumnName { get; } = UserIndexSql.GetColumnNameFor(fieldName);

	public ReadOnlyMemory<byte> TableNameUtf8 { get; } = Encoding.UTF8.GetBytes(UserIndexSql.GetTableNameFor(indexName));

	public void CreateUserIndex(DuckDBAdvancedConnection connection) {
		var query = new CreateUserIndexNonQuery(TableName, TField.GetCreateStatement(FieldColumnName));
		connection.ExecuteNonQuery(ref query);
	}

	public List<IndexQueryRecord> ReadUserIndexForwardsQuery(DuckDBConnectionPool db, ReadUserIndexQueryArgs args) {
		var query = new ReadUserIndexForwardsQuery(TableName, args.ExcludeFirst, args.Field.GetQueryStatement(FieldColumnName));
		using (db.Rent(out var connection))
			return connection.ExecuteQuery<ReadUserIndexQueryArgs, IndexQueryRecord, ReadUserIndexForwardsQuery>(ref query, args).ToList();
	}

	public List<IndexQueryRecord> ReadUserIndexBackwardsQuery(DuckDBConnectionPool db, ReadUserIndexQueryArgs args) {
		var query = new ReadUserIndexBackwardsQuery(TableName, args.ExcludeFirst, args.Field.GetQueryStatement(FieldColumnName));
		using (db.Rent(out var connection))
			return connection.ExecuteQuery<ReadUserIndexQueryArgs, IndexQueryRecord, ReadUserIndexBackwardsQuery>(ref query, args).ToList();
	}

	public GetCheckpointResult? GetCheckpoint(DuckDBAdvancedConnection connection, GetCheckpointQueryArgs args) {
		return connection.QueryFirstOrDefault<GetCheckpointQueryArgs, GetCheckpointResult, GetCheckpointQuery>(args);
	}

	public static void SetCheckpoint(DuckDBAdvancedConnection connection, SetCheckpointQueryArgs args) {
		connection.ExecuteNonQuery<SetCheckpointQueryArgs, SetCheckpointNonQuery>(args);
	}

	public GetLastIndexedRecordResult? GetLastIndexedRecord(DuckDBAdvancedConnection connection) {
		var query = new GetLastIndexedRecordQuery(TableName);
		return connection.ExecuteQuery<GetLastIndexedRecordResult, GetLastIndexedRecordQuery>(ref query).FirstOrDefault();
	}
}

file readonly record struct CreateUserIndexNonQuery(string TableName, string CreateFieldStatement) : IDynamicParameterlessStatement {
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
		args[1] = CreateFieldStatement;
	}
}

file readonly record struct DeleteUserIndexNonQuery(string TableName) : IDynamicParameterlessStatement {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		drop table if exists "{0}"
		""");

	public void FormatCommandTemplate(Span<object?> args) =>
		args[0] = TableName;
}

internal record struct ReadUserIndexQueryArgs(long StartPosition, long EndPosition, bool ExcludeFirst, int Count, IField Field);

file readonly record struct ReadUserIndexForwardsQuery(string TableName, bool ExcludeFirst, string FieldQuery)
	: IDynamicQuery<ReadUserIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		select log_position, commit_position, event_number
		from "{0}"
		where log_position >{1} ? and log_position < ? {2}
		order by rowid limit ?
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = FieldQuery;
	}

	public static BindingContext Bind(in ReadUserIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		statement.Bind(index++, args.EndPosition);
		args.Field.BindTo(statement, ref index);
		statement.Bind(index, args.Count);

		return new BindingContext(statement, completed: true);
	}

	public static IndexQueryRecord Parse(ref DataChunk.Row row) =>
		new(row.ReadInt64(),
			row.TryReadInt64(),
			row.ReadInt64());
}

file readonly record struct ReadUserIndexBackwardsQuery(string TableName, bool ExcludeFirst, string FieldQuery)
	: IDynamicQuery<ReadUserIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		select log_position, commit_position, event_number
		from "{0}"
		where log_position <{1} ? {2}
		order by rowid desc
		limit ?
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = FieldQuery;
	}

	public static BindingContext Bind(in ReadUserIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		args.Field.BindTo(statement, ref index);
		statement.Bind(index, args.Count);

		return new BindingContext(statement, completed: true);
	}

	public static IndexQueryRecord Parse(ref DataChunk.Row row) =>
		new(row.ReadInt64(),
			row.TryReadInt64(),
			row.ReadInt64());
}

internal record struct GetCheckpointQueryArgs(string IndexName);

internal record struct GetCheckpointResult(long PreparePosition, long? CommitPosition, long Timestamp);

file struct GetCheckpointQuery : IQuery<GetCheckpointQueryArgs, GetCheckpointResult> {
	public static BindingContext Bind(in GetCheckpointQueryArgs args, PreparedStatement statement) =>
		new(statement) {
			args.IndexName,
		};

	public static ReadOnlySpan<byte> CommandText =>
		"""
		select log_position, commit_position, created
		from idx_user_checkpoints
		where index_name = ?
		limit 1
		"""u8;

	public static GetCheckpointResult Parse(ref DataChunk.Row row) =>
		new(row.ReadInt64(),
			row.TryReadInt64(),
			row.ReadInt64());
}

internal record struct SetCheckpointQueryArgs(string IndexName, long PreparePosition, long? CommitPosition, long Created);

file struct SetCheckpointNonQuery : IPreparedStatement<SetCheckpointQueryArgs> {
	public static BindingContext Bind(in SetCheckpointQueryArgs args, PreparedStatement statement) =>
		new(statement) {
			args.IndexName,
			args.PreparePosition,
			args.CommitPosition,
			args.Created,
		};

	public static ReadOnlySpan<byte> CommandText =>
		"""
		insert or replace
		into idx_user_checkpoints (index_name,log_position,commit_position,created)
		VALUES ($1,$2,$3,$4)
		"""u8;
}

internal record struct DeleteCheckpointNonQueryArgs(string IndexName);

file struct DeleteCheckpointNonQuery : IPreparedStatement<DeleteCheckpointNonQueryArgs> {
	public static BindingContext Bind(in DeleteCheckpointNonQueryArgs args, PreparedStatement statement) =>
		new(statement) {
			args.IndexName,
		};

	public static ReadOnlySpan<byte> CommandText =>
		"""
		delete
		from idx_user_checkpoints
		where index_name = ?
		"""u8;
}

internal record struct GetLastIndexedRecordResult(long PreparePosition, long? CommitPosition, long Timestamp);

file readonly record struct GetLastIndexedRecordQuery(string TableName) : IDynamicQuery<GetLastIndexedRecordResult> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		select log_position, commit_position, created
		from "{0}"
		order by rowid desc
		limit 1
		""");

	public void FormatCommandTemplate(Span<object?> args) => args[0] = TableName;

	public static GetLastIndexedRecordResult Parse(ref DataChunk.Row row) =>
		new(row.ReadInt64(),
			row.TryReadInt64(),
			row.ReadInt64());
}
