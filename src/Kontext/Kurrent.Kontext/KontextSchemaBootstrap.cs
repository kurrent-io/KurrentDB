// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Bootstraps the store: composes the DuckDB migration engine over the data source —
/// journal, stream, knobs — runs the pending steps, then asserts the version-retention
/// policy from configuration. The schema itself lives in KontextSchema.cs; registering a
/// new step is one line in <see cref="EnsureAsync"/>.
/// </summary>
public sealed class KontextSchemaBootstrap(KontextDataSource dataSource, ILoggerFactory? loggerFactory = null, bool forceReset = false) {
    public Task EnsureAsync(CancellationToken ct = default) =>
        new DuckDBMigrationEngine(new() {
            Context       = dataSource,
            Journal       = new DuckDBSchemaJournal(dataSource),
            LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance,
            ForceReset    = forceReset,

            // The stream. Append at the bottom; versions never reuse, bodies never change.
            Steps = [
                new KontextSchemaTask(), // v1 — tables, eager indexes, retention policy
            ],
        }).EnsureAsync(ct);
}
