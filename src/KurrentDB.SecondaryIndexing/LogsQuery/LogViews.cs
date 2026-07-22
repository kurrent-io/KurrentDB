// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.LogsQuery;

// Exposes the node's CLEF log files as the kdb.logs / kdb.stats views (the rewriter maps those
// names here). The views are TEMP and (re)created per query on the executing connection, like
// idx_all_snapshot: a static startup view can't work because read_json errors on an empty file set
// (fresh node / DisableLogFile) and can't reflect files that rotate in/out. With no matching files
// the view falls back to a typed zero-row source.
internal sealed class LogViews(string logsDir) {
	public const string LogsViewName = "__logs";
	public const string StatsViewName = "__stats";

	public void Create(DuckDBAdvancedConnection connection, bool logs, bool stats) {
		if (!logs && !stats)
			return;

		if (logs)
			Exec(connection, ViewSql(LogsViewName, MatchingFiles(IsMainLog)));
		if (stats)
			Exec(connection, ViewSql(StatsViewName, MatchingFiles(IsStatsLog)));
	}

	// log-err* / log-stats* siblings start "log-", so excluding those (rather than matching a date)
	// keeps this correct for any RollingInterval, including Infinite (a bare, undated log.json).
	private static bool IsMainLog(string name) =>
		name.StartsWith("log", StringComparison.Ordinal) && !name.StartsWith("log-", StringComparison.Ordinal);

	private static bool IsStatsLog(string name) => name.StartsWith("log-stats", StringComparison.Ordinal);

	private IReadOnlyList<string> MatchingFiles(Func<string, bool> match) {
		if (!Directory.Exists(logsDir))
			return [];

		try {
			var files = new List<string>();
			foreach (var path in Directory.EnumerateFiles(logsDir, "log*.json"))
				if (match(Path.GetFileName(path)))
					files.Add(path);

			return files;
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			// An unreadable log dir degrades to empty views, like a missing dir or read errors
			// (ignore_errors) - reading logs must never fail an otherwise valid query.
			return [];
		}
	}

	private static string ViewSql(string viewName, IReadOnlyList<string> files) {
		var source = files.Count > 0
			? $"read_json_objects([{string.Join(", ", files.Select(SqlString))}], format='newline_delimited', filename=true, ignore_errors=true)"
			: "(SELECT NULL::JSON AS json, NULL::VARCHAR AS filename WHERE false)";

		return $"CREATE OR REPLACE TEMP VIEW {viewName} AS {Projection} FROM {source};";
	}

	private static string SqlString(string path) => $"'{path.Replace('\\', '/').Replace("'", "''")}'";

	private const string Projection = """
		SELECT
			TRY_CAST(json->>'@t' AS TIMESTAMPTZ) AS timestamp,
			coalesce(json->>'@l', 'Information') AS level,
			render_message(json->>'@mt', json) AS message,
			json->>'@mt' AS message_template,
			TRY_CAST(json->>'@i' AS UBIGINT) AS event_id,
			json->>'@x' AS exception,
			json->>'SourceContext' AS source_context,
			TRY_CAST(json->>'ProcessId' AS BIGINT) AS process_id,
			TRY_CAST(json->>'ThreadId' AS BIGINT) AS thread_id,
			regexp_extract(filename, '[^/\\]+$') AS file,
			json AS raw
		""";

	private static void Exec(DuckDBAdvancedConnection connection, string sql) =>
		connection.ExecuteAdHocNonQuery(sql, multipleStatements: true);
}
