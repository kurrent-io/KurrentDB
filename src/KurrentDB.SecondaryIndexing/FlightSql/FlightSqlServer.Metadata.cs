// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Apache.Arrow.Flight.Sql;
using Arrow.Flight.Protocol.Sql;
using Google.Protobuf;
using KurrentDB.SecondaryIndexing.Indexes.User.Management;
using KurrentDB.SecondaryIndexing.Query;
using FlightSqlServerHelpers = Apache.Arrow.Flight.Sql.FlightSqlServer;

namespace KurrentDB.SecondaryIndexing.FlightSql;

// Catalog metadata commands: https://arrow.apache.org/docs/format/FlightSql.html
partial class FlightSqlServer {
	// Every table is backed by a DuckDB view, but for the client it is just a read-only table
	private const string TableType = "TABLE";

	// The ticket is the command itself, so DoGet can re-apply its filters
	private FlightInfo GetMetadataInfo(IMessage command, Schema schema, FlightDescriptor descriptor)
		=> new(schema, descriptor, [new FlightEndpoint(new FlightTicket(PackToAny(command)), [])]);

	private Task WriteDbSchemasAsync(CommandGetDbSchemas command, FlightServerRecordBatchStreamWriter writer) {
		IEnumerable<string> schemas = MatchesCatalog(command.Catalog)
			? engine.Tables
				.Select(static table => table.Schema)
				.Distinct()
				.Where(schema => MatchesPattern(command.DbSchemaFilterPattern, schema))
				.Order(StringComparer.Ordinal)
			: [];

		var catalogNames = new StringArray.Builder();
		var schemaNames = new StringArray.Builder();
		foreach (var schema in schemas) {
			catalogNames.AppendNull();
			schemaNames.Append(schema);
		}

		return WriteBatchAsync(writer, FlightSqlServerHelpers.DbSchemaFlightSchema, schemaNames.Length, catalogNames.Build(),
			schemaNames.Build());
	}

	private Task WriteTablesAsync(CommandGetTables command, FlightServerRecordBatchStreamWriter writer) {
		IEnumerable<TableInfo> tables = MatchesCatalog(command.Catalog)
		                                && (command.TableTypes is [] || command.TableTypes.Contains(TableType))
			? engine.Tables
				.Where(table => MatchesPattern(command.DbSchemaFilterPattern, table.Schema)
				                && MatchesPattern(command.TableNameFilterPattern, table.Name))
				.OrderBy(static table => table.Schema, StringComparer.Ordinal)
				.ThenBy(static table => table.Name, StringComparer.Ordinal)
			: [];

		var catalogNames = new StringArray.Builder();
		var schemaNames = new StringArray.Builder();
		var tableNames = new StringArray.Builder();
		var tableTypes = new StringArray.Builder();
		var tableSchemas = command.IncludeSchema ? new BinaryArray.Builder() : null;
		foreach (var table in tables) {
			// Resolve the schema before appending anything, so a skipped table leaves no partial row
			if (tableSchemas is not null) {
				Schema tableSchema;
				try {
					tableSchema = engine.GetTableSchema(table);
				} catch (UserIndexNotFoundException) {
					// the user index was deleted after the table list was taken
					continue;
				}

				tableSchemas.Append(SchemaExtensions.SerializeSchema(tableSchema).AsSpan());
			}

			catalogNames.AppendNull();
			schemaNames.Append(table.Schema);
			tableNames.Append(table.Name);
			tableTypes.Append(TableType);
		}

		var schema = FlightSqlServerHelpers.GetTableSchema(command.IncludeSchema);
		var length = tableNames.Length;
		return tableSchemas is null
			? WriteBatchAsync(writer, schema, length, catalogNames.Build(), schemaNames.Build(), tableNames.Build(), tableTypes.Build())
			: WriteBatchAsync(writer, schema, length,
				catalogNames.Build(), schemaNames.Build(), tableNames.Build(), tableTypes.Build(), tableSchemas.Build());
	}

	private static Task WriteTableTypesAsync(FlightServerRecordBatchStreamWriter writer)
		=> WriteBatchAsync(writer, FlightSqlServerHelpers.TableTypesSchema, 1, new StringArray.Builder().Append(TableType).Build());

	private static async Task WriteBatchAsync(FlightServerRecordBatchStreamWriter writer, Schema schema, int length, params IArrowArray[] columns) {
		await writer.SetupStream(schema);
		using var batch = new RecordBatch(schema, columns, length);
		await writer.WriteAsync(batch);
	}

	// Tables have no catalog: an empty string filters for tables without one, the same as no filter
	private static bool MatchesCatalog(ReadOnlySpan<char> catalog)
		=> catalog.IsEmpty;

	// An empty pattern is the same as no pattern: some clients (e.g. FlightSqlClient.GetTablesAsync) send
	// string.Empty instead of leaving the field unset, and no schema or table name is empty anyway
	private static bool MatchesPattern(ReadOnlySpan<char> pattern, ReadOnlySpan<char> value)
		=> pattern.IsEmpty || IsLikeMatch(value, pattern);

	// SQL LIKE semantics used by the Flight SQL filter patterns: '%' matches any substring,
	// '_' matches any single character, '\' escapes the next character (JDBC search string escape)
	private static bool IsLikeMatch(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern) {
		int inputIndex = 0, patternIndex = 0;

		// the position right after the last '%' and the input position it is currently matched up to
		int wildcardPatternIndex = -1, wildcardInputIndex = 0;

		while (inputIndex < input.Length) {
			if (patternIndex < pattern.Length) {
				switch (pattern[patternIndex]) {
					case '%':
						wildcardPatternIndex = ++patternIndex;
						wildcardInputIndex = inputIndex;
						continue;
					case '_':
						inputIndex++;
						patternIndex++;
						continue;
					case '\\' when patternIndex + 1 < pattern.Length:
						if (pattern[patternIndex + 1] == input[inputIndex]) {
							inputIndex++;
							patternIndex += 2;
							continue;
						}

						break;
					case var ch when ch == input[inputIndex]:
						inputIndex++;
						patternIndex++;
						continue;
				}
			}

			// mismatch: let the last '%' consume one more character, or fail if there is none
			if (wildcardPatternIndex < 0)
				return false;

			patternIndex = wildcardPatternIndex;
			inputIndex = ++wildcardInputIndex;
		}

		// the input is consumed, so the rest of the pattern may only be '%'
		return pattern[patternIndex..].TrimStart('%').IsEmpty;
	}
}
