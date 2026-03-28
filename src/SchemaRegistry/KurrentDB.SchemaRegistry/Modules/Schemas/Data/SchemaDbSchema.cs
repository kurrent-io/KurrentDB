// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Dapper;
using DuckDB.NET.Data;
using Kurrent.Quack;
using KurrentDB.DuckDB;

namespace KurrentDB.SchemaRegistry.Data;

[UsedImplicitly]
public class SchemaDbSchema : DuckDBOneTimeSetup {
	protected override void ExecuteCore(DuckDBAdvancedConnection connection) {
		const string createTablesAndIndexesSql =
			"""
			CREATE TABLE IF NOT EXISTS schema_versions (
			      version_id        TEXT        PRIMARY KEY
			    , schema_name       TEXT        NOT NULL
			    , version_number    INT         NOT NULL DEFAULT 0
			    , schema_definition BLOB        NOT NULL
			    , data_format       TINYINT     NOT NULL DEFAULT 0
			    , registered_at     TIMESTAMPTZ NOT NULL DEFAULT current_localtimestamp()
			    , checkpoint        UBIGINT     NOT NULL DEFAULT 0
			);
			CREATE TABLE IF NOT EXISTS schemas (
			      schema_name           TEXT        PRIMARY KEY
			    , description           TEXT
			    , data_format           TINYINT     NOT NULL DEFAULT 0
			    , latest_version_number INT         NOT NULL DEFAULT 0
			    , latest_version_id     TEXT        NOT NULL
			    , compatibility         TINYINT     NOT NULL
			    , tags                  JSON        NOT NULL DEFAULT '{}'
			    , created_at            TIMESTAMPTZ NOT NULL DEFAULT current_localtimestamp()
			    , updated_at            TIMESTAMPTZ
			    , checkpoint            UBIGINT     NOT NULL DEFAULT 0
			);
			""";
		connection.Execute(createTablesAndIndexesSql);
	}
}
