// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.RegularExpressions;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal abstract partial class UserIndexSql(string indexName, string fieldName) {
	// we validate the table/column names for safety reasons, although DuckDB allows a large set of characters when using quoted identifiers
	private static readonly Regex IdentifierRegex = ValidationRegex();

	public string TableName { get; } = GetTableNameFor(indexName);
	public string FieldColumnName { get; } = GetColumnNameFor(fieldName);
	public string ViewName { get; } = GetViewNameFor(indexName);

	public GetCheckpointResult? GetCheckpoint(DuckDBAdvancedConnection connection, in GetCheckpointQueryArgs args)
		=> connection.QueryFirstOrDefault<GetCheckpointQueryArgs, GetCheckpointResult, GetCheckpointQuery>(in args).ValueOrDefault;

	public static void SetCheckpoint(DuckDBAdvancedConnection connection, in SetCheckpointQueryArgs args)
		=> connection.ExecuteNonQuery<SetCheckpointQueryArgs, SetCheckpointNonQuery>(in args);

	public GetLastIndexedRecordResult? GetLastIndexedRecord(DuckDBAdvancedConnection connection) {
		var query = new GetLastIndexedRecordQuery(TableName);
		return connection.ExecuteQuery<GetLastIndexedRecordResult, GetLastIndexedRecordQuery>(ref query).FirstOrDefault().ValueOrDefault;
	}

	public void ReadUserIndexForwardsQuery(DuckDBAdvancedConnection connection,
		in ReadUserIndexQueryArgs args,
		ICollection<IndexQueryRecord> records) {
		var query = new ReadUserIndexForwardsQuery(ViewName, args.ExcludeFirst, args.Field.GetQueryStatement(FieldColumnName));
		connection
			.ExecuteQuery<ReadUserIndexQueryArgs, IndexQueryRecord, ReadUserIndexForwardsQuery>(ref query, in args)
			.CopyTo(records);
	}

	public void ReadUserIndexBackwardsQuery(DuckDBAdvancedConnection connection,
		in ReadUserIndexQueryArgs args,
		ICollection<IndexQueryRecord> records) {
		var query = new ReadUserIndexBackwardsQuery(ViewName, args.ExcludeFirst, args.Field.GetQueryStatement(FieldColumnName));
		connection
			.ExecuteQuery<ReadUserIndexQueryArgs, IndexQueryRecord, ReadUserIndexBackwardsQuery>(ref query, in args)
			.CopyTo(records);
	}

	private static string GetTableNameFor(string indexName) {
		var tableName = $"idx_user__{indexName}";

		return IdentifierRegex.IsMatch(tableName)
			? tableName
			: throw new($"Invalid table name: {tableName}");
	}

	private static string GetViewNameFor(string indexName) {
		var viewName = $"idx_user__{indexName}_view";

		return IdentifierRegex.IsMatch(viewName)
			? viewName
			: throw new($"Invalid view name: {viewName}");
	}

	private static string GetColumnNameFor(string fieldName) {
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

internal sealed class UserIndexSql<TField>(string indexName, string fieldName) : UserIndexSql(indexName, fieldName)
	where TField : IField {

	public void CreateUserIndex(DuckDBAdvancedConnection connection) {
		var query = new CreateUserIndexNonQuery(TableName, TField.GetCreateStatement(FieldColumnName));
		connection.ExecuteNonQuery(ref query);
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

internal record struct ReadUserIndexQueryArgs(long StartPosition, bool ExcludeFirst, int Count, IField Field);

file readonly record struct ReadUserIndexForwardsQuery(string TableName, bool ExcludeFirst, string FieldQuery)
	: IDynamicQuery<ReadUserIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		select log_position, commit_position, event_number
		from "{0}"
		where log_position >{1} ? {2}
		order by coalesce(commit_position, log_position) limit ?
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = FieldQuery;
	}

	public static StatementBindingResult Bind(in ReadUserIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		args.Field.BindTo(statement, ref index);
		statement.Bind(index, args.Count);

		return new(statement, completed: true);
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
		order by coalesce(commit_position, log_position) desc, log_position desc
		limit ?
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = FieldQuery;
	}

	public static StatementBindingResult Bind(in ReadUserIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		args.Field.BindTo(statement, ref index);
		statement.Bind(index, args.Count);

		return new(statement, completed: true);
	}

	public static IndexQueryRecord Parse(ref DataChunk.Row row) =>
		new(row.ReadInt64(),
			row.TryReadInt64(),
			row.ReadInt64());
}

internal record struct GetCheckpointQueryArgs(string IndexName);

internal record struct GetCheckpointResult(long PreparePosition, long? CommitPosition, long Timestamp);

file struct GetCheckpointQuery : IQuery<GetCheckpointQueryArgs, GetCheckpointResult> {
	public static StatementBindingResult Bind(in GetCheckpointQueryArgs args, PreparedStatement statement) =>
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
	public static StatementBindingResult Bind(in SetCheckpointQueryArgs args, PreparedStatement statement) =>
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
	public static StatementBindingResult Bind(in DeleteCheckpointNonQueryArgs args, PreparedStatement statement) =>
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
