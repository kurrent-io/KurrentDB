// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.RegularExpressions;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

/// A single equality constraint supplied by a query: this field must equal this (raw) value.
internal readonly record struct FieldConstraint(IField Field, string Value);

internal sealed partial class UserIndexSql {
	private const string TableNamePrefix = "idx_user__";

	// we validate the table/column names for safety reasons, although DuckDB allows a large set of characters when using quoted identifiers
	private static readonly Regex IdentifierRegex = ValidationRegex();

	private readonly IReadOnlyList<IField> _fields;

	public string TableName { get; }
	public string ViewName { get; }

	public UserIndexSql(string indexName, IReadOnlyList<IField> fields) {
		_fields = fields;
		TableName = GetTableNameFor(indexName);
		ViewName = GetViewNameFor(indexName);
	}

	public void CreateUserIndex(DuckDBAdvancedConnection connection) {
		// A single-field index keeps its column NOT NULL for backwards compatibility with existing tables
		// (the write path never indexes a null value for it). Multi-field indexes allow nulls per field.
		var nullable = _fields.Count != 1;

		var createStatements = new StringBuilder();
		foreach (var field in _fields)
			createStatements.Append(field.GetCreateStatement(nullable));

		var createTable = new CreateUserIndexNonQuery(TableName, createStatements.ToString());
		connection.ExecuteNonQuery(ref createTable);

		foreach (var field in _fields) {
			if (!field.OptimizeLookups)
				continue;

			var createIndex = new CreateFieldIndexNonQuery(GetFieldIndexNameFor(TableName, field.ColumnName), TableName, field.ColumnName);
			connection.ExecuteNonQuery(ref createIndex);
		}
	}

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
		var query = new ReadUserIndexForwardsQuery(ViewName, args.ExcludeFirst, BuildPredicate(args.Constraints));
		connection
			.ExecuteQuery<ReadUserIndexQueryArgs, IndexQueryRecord, ReadUserIndexForwardsQuery>(ref query, in args)
			.CopyTo(records);
	}

	public void ReadUserIndexBackwardsQuery(DuckDBAdvancedConnection connection,
		in ReadUserIndexQueryArgs args,
		ICollection<IndexQueryRecord> records) {
		var query = new ReadUserIndexBackwardsQuery(ViewName, args.ExcludeFirst, BuildPredicate(args.Constraints));
		connection
			.ExecuteQuery<ReadUserIndexQueryArgs, IndexQueryRecord, ReadUserIndexBackwardsQuery>(ref query, in args)
			.CopyTo(records);
	}

	// ANDs one equality per supplied field
	private static string BuildPredicate(IReadOnlyList<FieldConstraint> constraints) {
		if (constraints.Count == 0)
			return string.Empty;

		var predicate = new StringBuilder();
		foreach (var constraint in constraints)
			predicate.Append(constraint.Field.GetEqualityPredicate());
		return predicate.ToString();
	}

	public static bool IsUserIndexTable(ReadOnlySpan<char> tableName) => tableName.StartsWith(TableNamePrefix);

	private static string GetTableNameFor(string indexName) {
		var tableName = string.Concat(TableNamePrefix, indexName);

		return IdentifierRegex.IsMatch(tableName)
			? tableName
			: throw new($"Invalid table name: {tableName}");
	}

	internal static string GetViewNameFor(string indexName) {
		var viewName = $"idx_user__{indexName}_view";

		return IdentifierRegex.IsMatch(viewName)
			? viewName
			: throw new($"Invalid view name: {viewName}");
	}

	internal static string GetColumnNameFor(string fieldName) {
		var columnName = $"field_{fieldName}";

		return IdentifierRegex.IsMatch(columnName)
			? columnName
			: throw new($"Invalid column name: {columnName}");
	}

	private static string GetFieldIndexNameFor(string tableName, string columnName) {
		var indexName = $"{tableName}__{columnName}_idx";

		return IdentifierRegex.IsMatch(indexName)
			? indexName
			: throw new($"Invalid index name: {indexName}");
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

file readonly record struct CreateUserIndexNonQuery(string TableName, string CreateFieldStatements) : IDynamicParameterlessStatement {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		create table if not exists "{0}"
		(
			log_position bigint not null,
			commit_position bigint null,
			stream_revision bigint not null,
			created_at bigint not null
			{1},
			record_id blob not null
		)
		"""
	);

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = CreateFieldStatements;
	}
}

file readonly record struct CreateFieldIndexNonQuery(string IndexName, string TableName, string ColumnName) : IDynamicParameterlessStatement {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		create index if not exists "{0}" on "{1}" ("{2}")
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = IndexName;
		args[1] = TableName;
		args[2] = ColumnName;
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

internal record struct ReadUserIndexQueryArgs(long StartPosition, bool ExcludeFirst, int Count, IReadOnlyList<FieldConstraint> Constraints);

file readonly record struct ReadUserIndexForwardsQuery(string TableName, bool ExcludeFirst, string FieldsPredicate)
	: IDynamicQuery<ReadUserIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		select log_position, commit_position, stream_revision
		from "{0}"
		where log_position >{1} ? {2}
		order by coalesce(commit_position, log_position) limit ?
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = FieldsPredicate;
	}

	public static StatementBindingResult Bind(in ReadUserIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		foreach (var constraint in args.Constraints)
			constraint.Field.BindEquality(statement, ref index, constraint.Value);
		statement.Bind(index, args.Count);

		return new(statement, completed: true);
	}

	public static IndexQueryRecord Parse(ref DataChunk.Row row) =>
		new(row.ReadInt64(),
			row.TryReadInt64(),
			row.ReadInt64());
}

file readonly record struct ReadUserIndexBackwardsQuery(string TableName, bool ExcludeFirst, string FieldsPredicate)
	: IDynamicQuery<ReadUserIndexQueryArgs, IndexQueryRecord> {
	public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
		"""
		select log_position, commit_position, stream_revision
		from "{0}"
		where log_position <{1} ? {2}
		order by coalesce(commit_position, log_position) desc, log_position desc
		limit ?
		""");

	public void FormatCommandTemplate(Span<object?> args) {
		args[0] = TableName;
		args[1] = ExcludeFirst ? string.Empty : "=";
		args[2] = FieldsPredicate;
	}

	public static StatementBindingResult Bind(in ReadUserIndexQueryArgs args, PreparedStatement statement) {
		var index = 1;
		statement.Bind(index++, args.StartPosition);
		foreach (var constraint in args.Constraints)
			constraint.Field.BindEquality(statement, ref index, constraint.Value);
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
		select log_position, commit_position, created_at
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
