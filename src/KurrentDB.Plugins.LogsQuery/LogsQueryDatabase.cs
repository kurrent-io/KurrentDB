// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.Plugins.LogsQuery;

// A dedicated, sandboxed in-memory DuckDB over the node's own CLEF log files.
//
// Isolation is enforced entirely at connection setup (see Initialize): external
// filesystem access is disabled except for the log directory, extension loading is
// off, resources are capped, and the configuration is locked so a query cannot undo
// any of it. Writes (COPY TO) are additionally blocked at the API layer by the
// read-only statement wrapping in LogsQueryService.
internal sealed class LogsQueryDatabase : DuckDBConnectionPool {
	// Order matters: enable_external_access must be turned off only after
	// allowed_directories is set, and lock_configuration must be last or a query
	// could SET the earlier options back.
	const int MemoryLimitMb = 512;
	const int Threads = 2;

	readonly string _logsDir;
	readonly string _dbPath;

	LogsQueryDatabase(string logsDir, string dbPath) : base($"Data Source={dbPath}") {
		_logsDir = logsDir;
		_dbPath = dbPath;
		Capacity = 2; // caps concurrent queries
	}

	// Quack's pool rejects ":memory:"; back the sandbox with a throwaway temp file
	// instead. It is opened at connection creation (before the sandbox is locked), so
	// its own handle is exempt from the enable_external_access gate applied below.
	public static LogsQueryDatabase Create(string logsDir) {
		var dbPath = Path.GetTempFileName();
		File.Delete(dbPath);
		return new(logsDir, dbPath);
	}

	protected override void Dispose(bool disposing) {
		base.Dispose(disposing); // release the DB handle before deleting its file
		if (disposing) {
			foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*")) {
				try { File.Delete(f); } catch (IOException) { /* let the OS clean up */ }
			}
		}
	}

	protected override void Initialize(DuckDBAdvancedConnection connection) {
		base.Initialize(connection);

		// json reader is statically linked in DuckDB.NET.Data.Full; load explicitly
		// before extension autoloading is disabled below.
		Exec(connection, "LOAD json;");

		Exec(connection, $"SET memory_limit='{MemoryLimitMb}MB';");
		Exec(connection, $"SET threads={Threads};");
		Exec(connection, "SET autoinstall_known_extensions=false;");
		Exec(connection, "SET autoload_known_extensions=false;");
		Exec(connection, "SET allow_community_extensions=false;");
		Exec(connection, "SET allow_unsigned_extensions=false;");
		Exec(connection, "SET temp_directory='';"); // no spill to disk

		var dir = _logsDir.Replace("'", "''");
		Exec(connection, $"SET allowed_directories=['{dir}'];");
		Exec(connection, "SET enable_external_access=false;");

		CreateSchema(connection, dir);

		Exec(connection, "SET lock_configuration=true;"); // must be last
	}

	static void CreateSchema(DuckDBAdvancedConnection connection, string dir) {
		// Render a CLEF message template against its properties. Approximate: format
		// specifiers (padding, numeric formats) are dropped. A managed scalar function
		// is the production path for exact fidelity.
		Exec(connection, """
			CREATE MACRO render_message(mt, j) AS (
				list_reduce(
					list_prepend(mt, regexp_extract_all(mt, '\{@?\$?([A-Za-z0-9_]+)[^}]*\}', 1)),
					lambda acc, tok: regexp_replace(acc, '\{@?\$?' || tok || '[^}]*\}',
						coalesce(json_extract_string(j, '$."' || tok || '"'), ''), 'g')
				)
			);
			""");

		CreateView(connection, "logs", dir, "log[0-9]*.json");
		CreateView(connection, "errors", dir, "log-err*.json");
		CreateView(connection, "stats", dir, "log-stats*.json");
	}

	static void CreateView(DuckDBAdvancedConnection connection, string name, string dir, string glob) =>
		Exec(connection, $"""
			CREATE VIEW {name} AS
			SELECT
				(json->>'@t')::TIMESTAMPTZ AS timestamp,
				coalesce(json->>'@l', 'Information') AS level,
				render_message(json->>'@mt', json) AS message,
				json->>'@mt' AS message_template,
				(json->>'@i')::UBIGINT AS event_id,
				json->>'@x' AS exception,
				json->>'SourceContext' AS source_context,
				(json->>'ProcessId')::BIGINT AS process_id,
				(json->>'ThreadId')::BIGINT AS thread_id,
				regexp_extract(filename, '[^/]+$') AS file,
				json AS raw
			FROM read_json('{dir}/{glob}', format='newline_delimited', records=false,
				filename=true, ignore_errors=true);
			""");

	static void Exec(DuckDBAdvancedConnection connection, string sql) =>
		connection.ExecuteAdHocNonQuery(sql, multipleStatements: true);
}
