// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Nodes;
using DotNext.Buffers;
using Kurrent.Quack;
using Kurrent.Quack.Parser;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private MemoryOwner<byte> RewriteQuery(ReadOnlySpan<byte> queryUtf8, ref PreparedQueryBuilder builder) {
		JsonNode? tree;

		// Obtain AST
		using (sharedPool.Rent(out var connection)) {
			tree = connection.ParseSyntaxTree(queryUtf8);
		}

		// Transform AST
		switch (tree?["error"]?.GetValueKind()) {
			case JsonValueKind.False:
				RewriteNode(tree, ref builder);
				break;
			case JsonValueKind.True:
				throw new QuerySyntaxException(tree["error_message"]?.ToString() ?? string.Empty) {
					Type = tree["error_type"]?.ToString() ?? string.Empty,
					SubType = tree["error_subtype"]?.ToString() ?? string.Empty,
					Position = tree["position"]?.ToString() ?? string.Empty,
				};
			default:
				throw QuerySyntaxException.InvalidAst();
		}

		// Convert AST back to the query
		using (sharedPool.Rent(out var connection)) {
			return connection.FromSyntaxTree(tree);
		}
	}

	private void RewriteNode(JsonNode ast, ref PreparedQueryBuilder builder) {
		switch (ast) {
			case JsonObject obj:
				RewriteNode(obj, ref builder);
				break;
			case JsonArray array:
				RewriteNode(array, ref builder);
				break;
		}
	}

	private void RewriteNode(JsonObject node, ref PreparedQueryBuilder builder) {
		foreach (var (propertyName, propertyValue) in node) {
			if (propertyValue is null) {
				// nothing to do
			} else if (propertyName is "from_table") {
				RewriteFromClause(propertyValue, ref builder);
			} else {
				RewriteNode(propertyValue, ref builder);
			}
		}
	}

	private void RewriteNode(JsonArray array, ref PreparedQueryBuilder builder) {
		foreach (var element in array) {
			if (element is not null)
				RewriteNode(element, ref builder);
		}
	}

	private void RewriteFromClause(JsonNode fromClause, ref PreparedQueryBuilder builder) {
		// https://duckdb.org/docs/stable/sql/query_syntax/from
		// The following cases are possible:
		// TABLE_FUNCTION - not allowed
		// BASE_TABLE - allowed, the only allowed schemas are 'kdb' and 'usr'
		// JOIN - contains 'left' and 'right' sub-objects, apply rewrite recursively
		// SUBQUERY - allowed

		switch (fromClause["type"]?.ToString()) {
			case "BASE_TABLE":
				RewriteTableReference(fromClause, ref builder);
				break;
			case "JOIN":
				var left = fromClause["left"] ?? throw QuerySyntaxException.InvalidAst();
				var right = fromClause["right"] ?? throw QuerySyntaxException.InvalidAst();
				RewriteNode(left, ref builder);
				RewriteNode(right, ref builder);
				break;
			case "SUBQUERY":
				var subQuery = fromClause["subquery"] ?? throw QuerySyntaxException.InvalidAst();
				RewriteNode(subQuery, ref builder);
				break;
			case var sourceType:
				throw new UnsupportedQueryDataSourceTypeException(sourceType);
		}
	}

	private void RewriteTableReference(JsonNode tableReference, ref PreparedQueryBuilder builder) {
		const string schemaNameProperty = "schema_name";
		const string tableNameProperty = "table_name";

		// validate schema name
		switch (tableReference[schemaNameProperty]?.ToString()) {
			case "kdb":
				// rewrite system table name
				var tableName = tableReference[tableNameProperty]?.ToString() ?? string.Empty;
				tableName = RewriteSystemTableName(tableName, ref builder);
				tableReference[tableNameProperty] = tableName;
				break;
			case "usr":
				// rewrite user index
				tableName = tableReference[tableNameProperty]?.ToString() ?? string.Empty;
				tableName = UserIndexSql.GetViewNameFor(tableName);
				builder.AddUserIndexViewName(tableName);
				tableReference[tableNameProperty] = tableName;
				break;
			case var schemaName:
				throw new UnsupportedSchemaException(schemaName);
		}

		tableReference[schemaNameProperty] = string.Empty;
	}

	private string RewriteSystemTableName(string tableName, ref PreparedQueryBuilder builder) {
		switch (tableName) {
			case "records":
				builder.HasDefaultIndex = true;
				return DefaultSql.DefaultIndexViewName;
			default:
				throw new UnsupportedSystemTableException(tableName);
		}
	}
}
