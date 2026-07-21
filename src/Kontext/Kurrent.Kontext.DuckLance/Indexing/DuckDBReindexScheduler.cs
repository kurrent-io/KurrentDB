using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// A background maintenance scheduler for a single Lance dataset: on a fixed cadence it creates any missing
/// vector index (once the collection has grown enough rows to train one), folds newly written rows into existing
/// vector indexes (or periodically retrains them), and runs always-on dataset compaction and vacuum.
/// One scheduler exists per dataset, owned and disposed by the parent <see cref="DuckDBVectorStore"/>.
/// </summary>
sealed class DuckDBReindexScheduler : IDisposable {
    // What to do on each tick is decided by the pure, database-free DuckDBReindexDecision.Decide; this class
    // is the only component that executes SQL, gathers the snapshot the decision needs, and owns the timer.

    readonly DuckDBConnectionManager _connectionManager;
    readonly string                  _datasetPath;

    // The collection's lazy vector-index creation entry point, invoked each tick as the catch-up path that
    // creates any missing vector index once the dataset has enough rows to train one.
    readonly Func<CancellationToken, Task<int>> _ensureVectorIndexesAsync;

    readonly ILogger              _logger;
    readonly DuckDBReindexOptions _options;
    readonly string               _qualifiedTableName;
    readonly string               _tableName;
    readonly TimeProvider         _timeProvider;
    readonly ITimer               _timer;

    // Interlocked disposal flag: 0 = live, 1 = disposed.
    int _disposed;

    // The stored last-retrain clock; only ever read/written inside the serialized tick body.
    DateTimeOffset _lastRetrain;

    // Interlocked non-overlap gate: 0 = idle, 1 = a tick is running.
    int _tickGate;

    public DuckDBReindexScheduler(
        DuckDBConnectionManager connectionManager,
        string datasetPath,
        string qualifiedTableName,
        Func<CancellationToken, Task<int>> ensureVectorIndexesAsync,
        DuckDBReindexOptions options,
        TimeProvider? timeProvider = null
    ) {
        ArgumentException.ThrowIfNullOrEmpty(datasetPath);
        ArgumentException.ThrowIfNullOrEmpty(qualifiedTableName);

        _connectionManager        = connectionManager;
        _datasetPath              = datasetPath;
        _qualifiedTableName       = qualifiedTableName;
        _ensureVectorIndexesAsync = ensureVectorIndexesAsync;
        _options                  = options;

        // Tests supply a fake TimeProvider for deterministic clocks and timers.
        _timeProvider = timeProvider ?? TimeProvider.System;

        _logger = options.LoggerFactory?.CreateLogger(typeof(DuckDBReindexScheduler)) ?? NullLogger.Instance;

        // The unqualified table name (for the duckdb_tables existence probe) is the last dotted segment of the
        // qualified name; the dataset resolver guarantees the {alias}.main.{tableName} shape.
        var lastDot = qualifiedTableName.LastIndexOf('.');
        _tableName = lastDot >= 0 ? qualifiedTableName[(lastDot + 1)..] : qualifiedTableName;

        // Start the retrain clock now, so the first retrain falls one RetrainInterval into the future rather than
        // on the first tick.
        _lastRetrain = _timeProvider.GetUtcNow();

        _timer = _timeProvider.CreateTimer(
            OnTimerTick, null, options.TickInterval,
            options.TickInterval);
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Disposing the timer stops future ticks. An in-flight tick body observes the disposal flag and returns
        // quietly, and in any case never throws, so no synchronization with a running tick is required here.
        _timer.Dispose();
    }

    /// <summary>
    /// Runs one tick body immediately, through the same non-overlap gate the timer uses — for deterministic
    /// tests; a background caller normally lets the timer drive ticks.
    /// </summary>
    public Task TickNowAsync(CancellationToken cancellationToken = default) => RunTickGuardedAsync(cancellationToken);

    void OnTimerTick(object? state) =>
        // Fire-and-forget: the guarded tick body never throws, so nothing can escape onto the timer thread.
        _ = RunTickGuardedAsync(CancellationToken.None);

    async Task RunTickGuardedAsync(CancellationToken cancellationToken) {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        // Non-overlapping ticks: if a tick is already running, skip this one entirely.
        if (Interlocked.CompareExchange(ref _tickGate, 1, 0) != 0)
            return;

        try {
            await RunTickBodyAsync(cancellationToken).ConfigureAwait(false);
        } finally {
            Interlocked.Exchange(ref _tickGate, 0);
        }
    }

    /// <summary>
    /// The single tick body. Never throws — every failure is caught and logged; quietly skips when the
    /// collection's table has not been created yet (the common pre-<c>EnsureCollectionExists</c> case).
    /// </summary>
    [SuppressMessage(
        "Design", "CA1031:Do not catch general exception types",
        Justification = "A background maintenance tick must never surface an exception onto the timer thread; every failure is logged and swallowed by design.")]
    async Task RunTickBodyAsync(CancellationToken cancellationToken) {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try {
            // Skip quietly if the collection's table has never been created (no dataset yet).
            var tableExists = await _connectionManager
                .ExecuteAsync(TableExists, cancellationToken)
                .ConfigureAwait(false);

            if (!tableExists) {
                _logger.LogDebug(
                    "DuckLance reindex tick skipped: table {QualifiedTableName} does not exist yet.",
                    _qualifiedTableName);

                return;
            }

            // (a) Catch-up creation: create any missing vector index now that the dataset may hold enough rows.
            await _ensureVectorIndexesAsync(cancellationToken).ConfigureAwait(false);

            // (b) Gather the snapshot the pure decision needs: row count + vector indexes (scalar ones excluded).
            var (totalRows, vectorIndexes) =
                await _connectionManager
                    .ExecuteAsync(GatherState, cancellationToken)
                    .ConfigureAwait(false);

            // (c) Decide, purely.
            var now = _timeProvider.GetUtcNow();

            var decision = DuckDBReindexDecision.Decide(
                totalRows, vectorIndexes, _options,
                _lastRetrain, now);

            // (d)-(f) Execute the decision on a single rented connection: index ops, then compaction, then vacuum.
            await _connectionManager
                .ExecuteAsync(operation: connection => ApplyDecision(connection, decision), cancellationToken)
                .ConfigureAwait(false);

            // (g) Advance the retrain clock only when a retrain actually ran.
            if (decision.RetrainRan)
                _lastRetrain = now;
        } catch (Exception ex) {
            _logger.LogError(ex, "DuckLance reindex tick failed for dataset {DatasetPath}.", _datasetPath);
        }
    }

    /// <summary>Determines whether the collection's table currently exists in the attached Lance namespace.</summary>
    bool TableExists(DuckDBAdvancedConnection connection) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM duckdb_tables() WHERE database_name = ? AND table_name = ?";
        command.Parameters.Add(new DuckDBParameter(_connectionManager.StorageAlias));
        command.Parameters.Add(new DuckDBParameter(_tableName));

        var result = command.ExecuteScalar();
        var count  = result is not null ? (long)result : 0;
        return count > 0;
    }

    /// <summary>Reads the dataset's current row count and its vector indexes (scalar indexes excluded) for the decision.</summary>
    (long TotalRows, IReadOnlyList<(string IndexName, string Fields, long RowsIndexed)> VectorIndexes) GatherState(DuckDBAdvancedConnection connection) {
        long totalRows;

        using (DbCommand countCommand = connection.CreateCommand()) {
            countCommand.CommandText = $"SELECT count(*) FROM {_qualifiedTableName}";
            var result = countCommand.ExecuteScalar();
            totalRows = result is not null ? Convert.ToInt64(result, CultureInfo.InvariantCulture) : 0;
        }

        var vectorIndexes = new List<(string IndexName, string Fields, long RowsIndexed)>();

        using (DbCommand showCommand = connection.CreateCommand()) {
            showCommand.CommandText = $"SHOW INDEXES ON '{DuckDBMaintenanceSql.EscapePath(_datasetPath)}'";
            using var reader = showCommand.ExecuteReader();

            foreach (var row in DuckDBIndexBuilder.ParseShowIndexes(reader)) {
                if (DuckDBMaintenanceSql.IsVectorIndexType(row.Type))
                    vectorIndexes.Add((row.Name, row.Fields, row.RowsIndexed));
            }
        }

        return (totalRows, vectorIndexes);
    }

    /// <summary>Executes the decided per-index operations, then compaction, then vacuum, on one rented connection.</summary>
    void ApplyDecision(DuckDBAdvancedConnection connection, DuckDBReindexDecision decision) {
        foreach (var operation in decision.IndexOperations) {
            var sql = operation.Action == DuckDBReindexIndexAction.Retrain
                ? DuckDBMaintenanceSql.BuildRetrainOptimizeSql(operation.IndexName, _datasetPath)
                : DuckDBMaintenanceSql.BuildAppendOptimizeSql(operation.IndexName, _datasetPath);

            Execute(connection, sql);
        }

        if (decision.Compact)
            Execute(connection, DuckDBMaintenanceSql.BuildCompactionSql(_datasetPath));

        if (decision.Vacuum) {
            Execute(
                connection, DuckDBMaintenanceSql.BuildVacuumSql(
                    _datasetPath,
                    (long)_options.VacuumRetention.TotalSeconds,
                    _options.VacuumRetainVersions));
        }

        static void Execute(DuckDBAdvancedConnection connection, string sql) {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}