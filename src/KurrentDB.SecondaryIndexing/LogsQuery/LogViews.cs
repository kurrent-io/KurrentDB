// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.LogsQuery;

// Builds the SQL the rewriter inlines for kdb.logs / kdb.stats: the CLEF projection over a
// read_json_objects glob of the node's log directory, or a typed zero-row source when the directory
// has no matching files. read_json_objects hard-errors on an empty glob match (fresh node /
// DisableLogFile), so we must not emit it then - hence the C# gate. The glob is expanded by DuckDB
// at execute time, so a reused prepared statement tracks files that rotate in/out.
internal sealed class LogViews(string logsDir) {
	public string BuildLogsSql() => Build("log[!-]*json", IsMainLog);

	public string BuildStatsSql() => Build("log-stats*.json", IsStatsLog);

	private string Build(string glob, Func<string, bool> match) {
		var source = HasMatchingFile(match)
			? $"read_json_objects({SqlString(Path.Combine(logsDir, glob))}, format='newline_delimited', filename=true, ignore_errors=true)"
			: "(SELECT NULL::JSON AS json, NULL::VARCHAR AS filename WHERE false)";

		return $"{Projection} FROM {source}";
	}

	// Main logs are log*.json but not the log-err* / log-stats* siblings; excluding "log-" (rather
	// than matching a date) holds for any RollingInterval, including the undated log.json. The glob
	// 'log[!-]*json' has the same match set for real log files.
	private static bool IsMainLog(string name) =>
		name.StartsWith("log", StringComparison.Ordinal) && !name.StartsWith("log-", StringComparison.Ordinal);

	private static bool IsStatsLog(string name) => name.StartsWith("log-stats", StringComparison.Ordinal);

	// The gate: does the glob have anything to match? Mirrors the glob's selection so a positive gate
	// never yields a zero-match glob (which would throw at execute).
	private bool HasMatchingFile(Func<string, bool> match) {
		if (!Directory.Exists(logsDir))
			return false;

		try {
			foreach (var path in Directory.EnumerateFiles(logsDir, "log*.json"))
				if (match(Path.GetFileName(path)))
					return true;

			return false;
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			// An unreadable log dir degrades to the empty source - reading logs must never fail an
			// otherwise valid query.
			return false;
		}
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
}
