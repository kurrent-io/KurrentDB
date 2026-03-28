// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Represents a root class for all query preparation exceptions.
/// </summary>
public class QueryPreparationException : Exception {
	private protected QueryPreparationException(string message) : base(message) {

	}
}

/// <summary>
/// Indicates incorrect query syntax.
/// </summary>
public sealed class QuerySyntaxException : QueryPreparationException {
	internal QuerySyntaxException(string message)
		: base(message) {
	}

	/// <summary>
	/// Gets the type of the error.
	/// </summary>
	public required string Type { get; init; }

	/// <summary>
	/// Gets the type of the sub-error.
	/// </summary>
	public required string SubType { get; init; }

	/// <summary>
	/// Get the position within the query.
	/// </summary>
	public required string Position { get; init; }

	internal static QuerySyntaxException InvalidAst() => new("DuckDB doesn't produce a valid AST JSON tree") {
		Type = string.Empty,
		SubType = string.Empty,
		Position = "0",
	};
}

/// <summary>
/// Indicates that the specified data source in FROM clause is not supported, e.g. table function.
/// </summary>
public sealed class UnsupportedQueryDataSourceTypeException : QueryPreparationException {
	internal UnsupportedQueryDataSourceTypeException(string? sourceType)
		: base($"Unsupported query data source '{sourceType}'") => SourceType = sourceType;

	public string? SourceType { get; }
}

public sealed class UnsupportedSchemaException : QueryPreparationException {
	internal UnsupportedSchemaException(string? schemaName)
		: base($"Schema '{schemaName}' is not supported") => SchemaName = schemaName;

	public string? SchemaName { get; }
}

public sealed class UnsupportedSystemTableException : QueryPreparationException {
	internal UnsupportedSystemTableException(string tableName)
		: base($"System table '{tableName}' is not supported") => TableName = tableName;

	public string TableName { get; }
}
