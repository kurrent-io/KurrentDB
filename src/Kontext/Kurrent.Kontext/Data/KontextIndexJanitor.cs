// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data.LanceDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Data;

public sealed class KontextIndexJanitor : IDisposable {
    const string Table     = "memories";
    const string Column    = "embedding";
    const string FtsColumn = "content";

    const string QualifiedTable = "ldb.main.memories";

    static readonly LanceIvfPqIndexOptions MemoriesIndexOptions = new() {
        NumSubVectors = KontextIndexConstants.VectorsDimension / 8,
        NumPartitions = 1,
    };

    readonly KontextDataSource         _dataSource;
    readonly ILogger                   _logger;
    readonly KontextMaintenanceOptions _options;
    readonly TimeProvider              _timeProvider;
    readonly ITimer                    _timer;

    // Interlocked disposal flag: 0 = live, 1 = disposed.
    int _disposed;

    // The stored last-retrain clock; only ever read/written inside the serialized tick body.
    DateTimeOffset _lastRetrain;

    // Interlocked non-overlap gate: 0 = idle, 1 = a tick is running.
    int _tickGate;

    public KontextIndexJanitor(KontextDataSource dataSource, KontextMaintenanceOptions options, TimeProvider? timeProvider = null) {
        _dataSource = dataSource;
        _options    = options;

        // Tests supply a fake TimeProvider for deterministic clocks and timers.
        _timeProvider = timeProvider ?? TimeProvider.System;

        _logger = options.LoggerFactory?.CreateLogger(typeof(KontextIndexJanitor)) ?? NullLogger.Instance;

        // Start the retrain clock now, so the first retrain falls one RetrainInterval into the
        // future rather than on the first tick.
        _lastRetrain = _timeProvider.GetUtcNow();

        _timer = _timeProvider.CreateTimer(
            OnTimerTick, null, options.TickInterval,
            options.TickInterval);
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Disposing the timer stops future ticks. An in-flight tick body observes the disposal
        // flag and returns quietly, and in any case never throws, so no synchronization with a
        // running tick is required here.
        _timer.Dispose();
    }

    /// <summary>
    /// Runs one tick body immediately, through the same non-overlap gate the timer uses — for
    /// deterministic tests; a background caller normally lets the timer drive ticks.
    /// </summary>
    public Task TickNowAsync(CancellationToken ct = default) {
        RunTickGuarded();
        return Task.CompletedTask;
    }

    void OnTimerTick(object? state) =>
        // The tick runs synchronously on the timer's thread-pool thread; the guarded body never
        // throws, so nothing can escape onto it.
        RunTickGuarded();

    void RunTickGuarded() {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        // Non-overlapping ticks: if a tick is already running, skip this one entirely.
        if (Interlocked.CompareExchange(ref _tickGate, 1, 0) != 0)
            return;

        try {
            RunTickBody();
        } finally {
            Interlocked.Exchange(ref _tickGate, 0);
        }
    }

    // The single tick body. Never throws — every failure is caught and logged; quietly skips
    // while the memories table has not been created yet (a tick that fires before the host ran
    // the migration stream — KontextMigrations).
    void RunTickBody() {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try {
            _dataSource.Execute(connection => {
                if (connection.GetTableInfo(QualifiedTable) is not { } info) {
                    _logger.LogDebug("Kontext maintenance tick skipped: the memories table does not exist yet.");
                    return;
                }

                // The FTS tail is a correctness hole — over unfolded rows lance_fts returns the
                // first k rows by scan arrival, not the top k by score — so the fold runs FIRST,
                // always-on and never cadence-gated: a vector-index failure later in the tick must
                // not starve it. A zero tail is an engine no-op (no new dataset version).
                connection.EnsureInvertedIndex(QualifiedTable, FtsColumn);

                var index = info.FindIndex(LanceIndexNames.Vector(Column));

                var now    = _timeProvider.GetUtcNow();
                var action = Decide(info.RowCount, index?.RowsIndexed, _options, _lastRetrain, now);

                switch (action) {
                    case KontextMaintenanceAction.EnsureVectorIndex: {
                        var current = connection.EnsureVectorIndex(QualifiedTable, Column, MemoriesIndexOptions);

                        // A creation IS a full train: when the index was missing and now exists, start
                        // its retrain clock here so the first time-based rebuild falls one interval
                        // after creation instead of immediately following it.
                        if (index?.RowsIndexed is null && current)
                            _lastRetrain = now;

                        break;
                    }

                    case KontextMaintenanceAction.RetrainVectorIndex:
                        connection.OptimizeIndex(QualifiedTable, LanceIndexNames.Vector(Column), options => options.Mode = LanceOptimizeMode.Retrain);

                        // Advance only after the rebuild landed — a failed retrain must stay due.
                        _lastRetrain = now;
                        break;
                }

                // Compaction is cadence-independent, always-on hygiene — it runs on every tick
                // regardless of what the index needed.
                connection.CompactTable(QualifiedTable);
            });
        } catch (Exception ex) {
            // A background maintenance tick must never surface an exception onto the timer
            // thread; the next tick retries whatever this one left undone.
            _logger.LogError(ex, "Kontext maintenance tick failed.");
        }
    }

    /// <summary>
    /// The pure, database-free per-tick decision for the single vector index: nothing, an ensure
    /// (create the missing index, or fold a large-enough unindexed tail into it), or the
    /// time-based full retrain — which takes precedence over a fold when both are due.
    /// </summary>
    public static KontextMaintenanceAction Decide(
        long totalRows,
        long? vectorIndexRows,
        KontextMaintenanceOptions options,
        DateTimeOffset lastRetrain,
        DateTimeOffset now
    ) {
        // An empty table can neither train an index nor usefully optimize one.
        if (totalRows == 0)
            return KontextMaintenanceAction.None;

        // Catch-up creation: the schema owns the training floor (it asks the engine by trying),
        // so a missing index is always worth an ensure attempt — never a retrain.
        if (vectorIndexRows is null)
            return KontextMaintenanceAction.EnsureVectorIndex;

        if (now - lastRetrain >= options.RetrainInterval)
            return KontextMaintenanceAction.RetrainVectorIndex;

        // Fold only when the unindexed tail is BOTH a large enough fraction of the table AND a
        // large enough absolute count — the floor stops small tables from re-optimizing on every
        // handful of new rows. Ratio uses strict '>' (exactly at the threshold does NOT trigger);
        // floor uses '>=' (exactly at the floor DOES trigger).
        var unindexed = totalRows - vectorIndexRows.Value;

        return unindexed >= options.UnindexedRowFloor && (double)unindexed / totalRows > options.UnindexedRatioThreshold
            ? KontextMaintenanceAction.EnsureVectorIndex
            : KontextMaintenanceAction.None;
    }
}

/// <summary>The maintenance <see cref="KontextIndexJanitor.Decide"/> picks for the vector index on one tick.</summary>
public enum KontextMaintenanceAction {
    /// <summary>The vector index needs nothing on this tick.</summary>
    None,

    /// <summary>Run <see cref="KontextIndexMaintenance.EnsureVectorIndex"/>: create the missing index, or fold the unindexed tail into the existing one.</summary>
    EnsureVectorIndex,

    /// <summary>Run <see cref="KontextIndexMaintenance.RetrainVectorIndex"/>: the time-based full rebuild is due.</summary>
    RetrainVectorIndex,
}

/// <summary>
/// The maintenance loop's knobs. A mutable settings class by design — config binding does not
/// cope with records.
/// </summary>
public sealed class KontextMaintenanceOptions {
    /// <summary>The background tick period. Default is 5 minutes.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The fraction of unindexed rows that triggers an index fold. Default is 0.15 (15%).</summary>
    public double UnindexedRatioThreshold { get; set; } = 0.15;

    /// <summary>
    /// The minimum absolute number of unindexed rows before the ratio trigger may fire — avoids
    /// over-triggering on small tables. Default is 1000.
    /// </summary>
    public int UnindexedRowFloor { get; set; } = 1000;

    /// <summary>The time-based full retrain cadence. Default is 24 hours.</summary>
    public TimeSpan RetrainInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The optional logger factory for tick diagnostics; when null, nothing is logged.</summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
