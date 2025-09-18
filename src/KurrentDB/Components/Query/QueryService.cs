// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dapper;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Serilog;

namespace KurrentDB.Components.Query;

public static partial class QueryService {
	public static string AmendQuery(string query) {
		var matches = ExtractionRegex().Matches(query);
		List<string> ctes = [AllCte];
		foreach (Match match in matches) {
			if (!match.Success) continue;
			var tokens = match.Value.Split(':');
			var cteName = ReplaceSpecialCharsWithUnderscore($"{tokens[0]}_{tokens[1]}");
			var where = "where " + tokens[0] switch {
				"stream" => $"stream = '{tokens[1]}'",
				"category" => $"category = '{tokens[1]}'",
				_ => throw new("Invalid token")
			};
			var cte = string.Format(CteTemplate, cteName, where);
			ctes.Add(cte);
			query = query.Replace(match.Value, cteName);
		}

		return $"with\r\n{string.Join(",\r\n", ctes)}\r\n{query}";
	}

	internal static List<Dictionary<string, object>> ExecuteAdHocUserQuery(this DuckDBConnectionPool pool, string sql) {
		var result = pool.QueryFirstOrDefault<Sql2Json.Args, Sql2Json.Result, Sql2Json>(new(sql.ReplaceLineEndings("")));
		if (!result.HasValue) {
			throw new("Error parsing query");
		}

		var conversionResponse = JsonSerializer.Deserialize<SqlJsonResponse>(result.Value.Json);
		if (conversionResponse.Error) {
			var error = conversionResponse.ErrorMessage.StartsWith("Only SELECT")
				? "Only SELECT statements are allowed"
				: $"Error parsing query: {conversionResponse.ErrorMessage}";
			throw new(error);
		}

		using var scope = pool.Rent(out var connection);
		var query = AmendQuery(sql);
		var items = (IEnumerable<IDictionary<string, object>>)connection.Query(query);
		return items.Select(x => x.ToDictionary(y => y.Key, y => y.Value)).ToList();
	}

	private static string ReplaceSpecialCharsWithUnderscore(string input) {
		return string.IsNullOrEmpty(input) ? string.Empty : SpecialCharsRegex().Replace(input, "_");
	}

	[GeneratedRegex(@"\b(?:stream|category):([A-Za-z0-9_-]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ExtractionRegex();

	[GeneratedRegex("[^A-Za-z0-9_]", RegexOptions.CultureInvariant)]
	private static partial Regex SpecialCharsRegex();

	private static readonly string AllCte = string.Format(CteTemplate, "all_events", "");

	private const string CteTemplate = """
	                                   {0} AS (
	                                       select log_position, stream, event_number, epoch_ms(created) as created_at, event->>'data' as data, event->>'metadata' as metadata
	                                       from (
	                                           select *, kdb_get(log_position)::JSON as event
	                                           from (
	                                               select stream, event_number, log_position, created from idx_all {1}
	                                               union all
	                                               select stream, event_number, log_position, created from inflight() {1}
	                                           )
	                                       )
	                                   )
	                                   """;

	private record SqlJsonResponse {
		[JsonPropertyName("error")] public bool Error { get; init; }

		[JsonPropertyName("error_message")] public string ErrorMessage { get; init; } = "";
	}

	public struct Sql2Json : IQuery<Sql2Json.Args, Sql2Json.Result> {
		public record struct Args(string Sql);

		public record struct Result(string Json);

		public static BindingContext Bind(in Args args, PreparedStatement statement)
			=> new(statement) { args.Sql };

		public static ReadOnlySpan<byte> CommandText => "select json_serialize_sql($1::varchar)"u8;

		public static Result Parse(ref DataChunk.Row row) => new(row.TryReadString());
	}
}
