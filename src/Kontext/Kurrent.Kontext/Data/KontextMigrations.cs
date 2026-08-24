// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data.Migrations;
using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Data;

public sealed class KontextMigrations {
    public static DuckLanceMigrationEngine CreateEngine(KontextDataSource dataSource, ILogger? logger = null) {
        var engineOptions = new DuckLanceMigrationEngineOptions {
            Journal    = new DuckLanceSchemaJournal(dataSource),
            Context    = dataSource,
            Logger     = logger ?? NullLogger.Instance
        };

        engineOptions.ConfigureVersioned(x => {
            x.Enqueue<MemoriesInitialSchema>();
            x.Enqueue<RecordsInitialSchema>();
            x.Enqueue<AutoCleanupTables>();
            x.Enqueue<EntitiesInitialSchema>();
        });

        return new(engineOptions);
    }
}