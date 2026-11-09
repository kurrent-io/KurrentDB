// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNext.Buffers;
using Kurrent.Quack.Parser;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.User;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private const string SystemSchema = "kdb";
	private const string UserIndicesSchema = "usr";
	private const string DefaultIndexTableName = "records";
	private const string DefaultIndexTableFullName = $"{SystemSchema}.{DefaultIndexTableName}";

	private const string CatalogNameProperty = "catalog_name";
	private const string SchemaNameProperty = "schema_name";
	private const string TableNameProperty = "table_name";

	// lazily parsed TABLE_FUNCTION node for kdb_table_names(), cloned on every use
	private volatile JsonObject? _tableNamesFunctionReference;

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
				RewriteKnownNode(obj, ref builder);
				foreach (var property in obj) {
					RewriteNode(property.Value, ref builder);
				}

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
			case "SHOW_REF":
				RewriteMetaTableReference(ast);
				break;
			case "TABLE_FUNCTION":
				throw new UnsupportedQueryDataSourceTypeException("TABLE_FUNCTION");
			default:
				break;
		}
	}

	private static void RewriteTableReference(JsonNode tableReference, ref PreparedQueryBuilder builder) {
		// reject any explicit catalog qualifier (e.g. "memory.kdb.records" or an attached database);
		// unqualified references always serialize catalog_name as an empty string
		if (tableReference[CatalogNameProperty]?.ToString() is { Length: > 0 } catalogName)
			throw new UnsupportedCatalogException(catalogName);

		// validate schema name
		switch (tableReference[SchemaNameProperty]?.ToString()) {
			case SystemSchema:
				// rewrite system table name
				var tableName = tableReference[TableNameProperty]?.ToString() ?? string.Empty;
				tableName = RewriteSystemTableName(tableName, ref builder);
				tableReference[TableNameProperty] = tableName;
				break;
			case UserIndicesSchema:
				// rewrite user index
				tableName = tableReference[TableNameProperty]?.ToString() ?? string.Empty;
				tableName = UserIndexSql.GetViewNameFor(tableName);
				builder.AddUserIndexViewName(tableName);
				tableReference[TableNameProperty] = tableName;
				break;
			case var schemaName:
				throw new UnsupportedSchemaException(schemaName);
		}

		tableReference[SchemaNameProperty] = string.Empty;
	}

	private static string RewriteSystemTableName(string tableName, ref PreparedQueryBuilder builder) {
		switch (tableName) {
			case DefaultIndexTableName:
				builder.HasDefaultIndex = true;
				return DefaultSql.DefaultIndexViewName;
			default:
				throw new UnsupportedSystemTableException(tableName);
		}
	}

	private void RewriteMetaTableReference(JsonNode tableReference) {
		// reject any explicit catalog qualifier (e.g. "memory.kdb.records" or an attached database);
		// unqualified references always serialize catalog_name as an empty string
		if (tableReference[CatalogNameProperty]?.ToString() is { Length: > 0 } catalogName)
			throw new UnsupportedCatalogException(catalogName);

		if (tableReference[SchemaNameProperty]?.ToString() is { Length: > 0 } schemaName)
			throw new UnsupportedSchemaException(schemaName);

		// the parser keeps the identifier quoted: SHOW TABLES serializes as table_name = "\"tables\""
		var tableName = tableReference[TableNameProperty]?.ToString() ?? string.Empty;
		switch (tableName) {
			case "tables" or "\"tables\"":
				ReplaceShowTablesWithFunctionCall(tableReference.AsObject());
				break;
			default:
				throw new UnsupportedSystemTableException(tableName);
		}
	}

	// SHOW TABLES => SELECT * FROM kdb_table_names()
	private void ReplaceShowTablesWithFunctionCall(JsonObject showReference) {
		// Mutate the node in place: its parent is being enumerated by RewriteNode, so it can't be replaced there.
		// The TABLE_FUNCTION check in RewriteKnownNode has already run for this node, so the rewritten
		// reference is not rejected, and its children (the function call expression) contain no table references.
		showReference.Clear();
		foreach (var (name, value) in GetTableNamesFunctionReference()) {
			showReference[name] = value;
		}
	}

	// Every node is cloned, so it has no parent and can be attached to the rewritten tree
	private IEnumerable<KeyValuePair<string, JsonNode?>> GetTableNamesFunctionReference() {
		// Obtain the TABLE_FUNCTION node from the parser instead of hand-crafting it, so its layout
		// always matches the serialization format of the linked DuckDB version
		if (_tableNamesFunctionReference is not { } template) {
			_tableNamesFunctionReference = template = CreateTemplate();
		}

		return template.Select(static property => KeyValuePair.Create(property.Key, property.Value?.DeepClone()));

		JsonObject CreateTemplate() {
			const string tableNamesQuery = $"SELECT * FROM {QueryTablesFunction.Name}()";

			JsonNode? tree;
			var tableNamesQueryUtf8 = Encoding.UTF8.GetBytes(tableNamesQuery);
			using (sharedPool.Rent(out var connection)) {
				tree = connection.ParseSyntaxTree(tableNamesQueryUtf8);
			}

			var template = tree?["statements"]?[0]?["node"]?["from_table"]?.AsObject()
			           ?? throw new UnreachableException($"Unable to parse {QueryTablesFunction.Name} reference");

			// The template is read by concurrent queries, so it must be fully built before it is published.
			// Nodes backed by a JsonElement build their children lazily on the first read, which is not thread-safe.
			// A concurrent initialization race is benign: each thread publishes its own complete template.
			Materialize(template);
			return template;
		}

		// Enumerating an object or array forces it to build its children, so no lazy initialization
		// happens on later reads. JsonValue nodes are immutable and safe to read concurrently.
		static void Materialize(JsonNode? node) {
			switch (node) {
				case JsonObject obj:
					foreach (var property in obj) {
						Materialize(property.Value);
					}

					break;
				case JsonArray array:
					foreach (var element in array) {
						Materialize(element);
					}

					break;
			}
		}
	}
}
