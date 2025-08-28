// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KurrentDB.Services;

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
}
