// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNext.Buffers;
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

	private void RewriteNode(JsonNode? ast, ref PreparedQueryBuilder builder) {
		switch (ast) {
			case JsonObject obj:
				// Post-order: recurse into children first, then handle this node. A node we replace
				// (see SpliceLogSource) must not be re-descended - post-order means it isn't - and the
				// splice clears/re-adds this object's keys, which would throw mid-enumeration otherwise.
				foreach (var property in obj) {
					RewriteNode(property.Value, ref builder);
				}

				RewriteKnownNode(obj, ref builder);
				break;
			case JsonArray array:
				foreach (var element in array) {
					RewriteNode(element, ref builder);
				}

				break;
			default:
				// string/number/true/false/null: no rewriting
				break;
		}
	}

	private void RewriteKnownNode(JsonObject ast, ref PreparedQueryBuilder builder) {
		// https://duckdb.org/docs/stable/sql/query_syntax/from
		// The following cases are possible:
		// TABLE_FUNCTION - not allowed
		// BASE_TABLE - allowed, the only allowed schemas are 'kdb' and 'usr'
		// JOIN - contains 'left' and 'right' sub-objects, apply rewrite recursively
		// SUBQUERY - allowed
		// PIVOT/UNPIVOT - allowed, the source table is still subject to the same rules
		//
		// A BASE_TABLE/TABLE_FUNCTION reference can appear under any property name depending on the
		// enclosing construct (e.g. PIVOT wraps its source under "source", not "from_table"), so the
		// match below is keyed purely on the node's own "type" and applies regardless of propertyName.

		switch (ast["type"]?.ToString()) {
			case "BASE_TABLE":
				RewriteTableReference(ast, ref builder);
				break;
			case "TABLE_FUNCTION":
				throw new UnsupportedQueryDataSourceTypeException("TABLE_FUNCTION");
			default:
				break;
		}
	}

	private void RewriteTableReference(JsonNode tableReference, ref PreparedQueryBuilder builder) {
		const string catalogNameProperty = "catalog_name";
		const string schemaNameProperty = "schema_name";
		const string tableNameProperty = "table_name";

		// reject any explicit catalog qualifier (e.g. "memory.kdb.records" or an attached database);
		// unqualified references always serialize catalog_name as an empty string
		if (tableReference[catalogNameProperty]?.ToString() is { Length: > 0 } catalogName)
			throw new UnsupportedCatalogException(catalogName);

		// validate schema name
		switch (tableReference[schemaNameProperty]?.ToString()) {
			case "kdb":
				var tableName = tableReference[tableNameProperty]?.ToString() ?? string.Empty;
				// logs/stats are not real tables: replace the reference with an inlined read of the
				// node's own CLEF files. The whole node becomes a SUBQUERY, so return before the
				// schema/table fixups below.
				switch (tableName) {
					case "logs":
						SpliceLogSource(tableReference.AsObject(), logViews.BuildLogsSql(), defaultAlias: "logs");
						return;
					case "stats":
						SpliceLogSource(tableReference.AsObject(), logViews.BuildStatsSql(), defaultAlias: "stats");
						return;
				}

				// rewrite system table name (records)
				tableReference[tableNameProperty] = RewriteSystemTableName(tableName, ref builder);
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

	// Replaces a kdb.logs / kdb.stats BASE_TABLE node in place with an inlined SUBQUERY over the
	// node's CLEF files. Rather than hand-build the SUBQUERY node shape, parse a wrapper select and
	// lift its from_table (DuckDB produces the correct shape), then copy it over the base-table node.
	private void SpliceLogSource(JsonObject tableReference, string sourceSql, string defaultAlias) {
		var alias = tableReference["alias"]?.ToString();

		JsonNode lifted;
		using (sharedPool.Rent(out var connection))
			lifted = connection.ParseSyntaxTree(Encoding.UTF8.GetBytes($"SELECT * FROM ({sourceSql}) AS __t"))
				!["statements"]![0]!["node"]!["from_table"]!;

		foreach (var key in tableReference.Select(p => p.Key).ToArray())
			tableReference.Remove(key);
		foreach (var kv in lifted.AsObject())
			tableReference[kv.Key] = kv.Value?.DeepClone();

		// preserve the original reference's alias so qualified columns still resolve; an unaliased
		// reference defaults to the table name (so `logs.message` / `stats.x` keep working).
		tableReference["alias"] = string.IsNullOrEmpty(alias) ? defaultAlias : alias;
	}
}
