// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Data;

/// <summary>
/// The memories dataset's background maintenance loop: on a fixed cadence it creates the vector
/// index once the table has grown enough rows to train one, folds newly written rows into it (or
/// periodically retrains it), and runs always-on dataset compaction and vacuum.
///
/// The split mirrors the rest of the data layer:
/// - <see cref="KontextSchema"/> owns every DDL/maintenance statement — this class issues no SQL
/// - this class owns the clock, the tick cadence, and the pure per-tick decision (<see cref="Decide"/>)
///
/// A tick never throws: every failure is logged and the next tick simply tries again — which is
/// also the commit-conflict story. A maintenance operation that loses a Lance commit race against
/// the projector fails its tick, and the cadence IS the retry.
/// </summary>
public sealed class KontextMaintenanceScheduler : IDisposable {
    readonly ILogger                   _logger;
    readonly KontextMaintenanceOptions _options;
    readonly KontextSchema             _schema;
    readonly TimeProvider              _timeProvider;
    readonly ITimer                    _timer;

    // Interlocked disposal flag: 0 = live, 1 = disposed.
    int _disposed;

    // The stored last-retrain clock; only ever read/written inside the serialized tick body.
    DateTimeOffset _lastRetrain;

    // Interlocked non-overlap gate: 0 = idle, 1 = a tick is running.
    int _tickGate;

    public KontextMaintenanceScheduler(KontextSchema schema, KontextMaintenanceOptions options, TimeProvider? timeProvider = null) {
        _schema  = schema;
        _options = options;

        // Tests supply a fake TimeProvider for deterministic clocks and timers.
        _timeProvider = timeProvider ?? TimeProvider.System;

        _logger = options.LoggerFactory?.CreateLogger(typeof(KontextMaintenanceScheduler)) ?? NullLogger.Instance;

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
    public Task TickNowAsync(CancellationToken ct = default) => RunTickGuardedAsync(ct);

    void OnTimerTick(object? state) =>
        // Fire-and-forget: the guarded tick body never throws, so nothing can escape onto the timer thread.
        _ = RunTickGuardedAsync(CancellationToken.None);

    async Task RunTickGuardedAsync(CancellationToken ct) {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        // Non-overlapping ticks: if a tick is already running, skip this one entirely.
        if (Interlocked.CompareExchange(ref _tickGate, 1, 0) != 0)
            return;

        try {
            await RunTickBodyAsync(ct).ConfigureAwait(false);
        } finally {
            Interlocked.Exchange(ref _tickGate, 0);
        }
    }

    // The single tick body. Never throws — every failure is caught and logged; quietly skips
    // while the memories table has not been created yet (a tick that fires before the host ran
    // KontextSchema.CreateAsync).
    async Task RunTickBodyAsync(CancellationToken ct) {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try {
            if (!await _schema.ExistsAsync(ct).ConfigureAwait(false)) {
                _logger.LogDebug("Kontext maintenance tick skipped: the memories table does not exist yet.");
                return;
            }

            var (totalRows, vectorIndexRows) = await _schema.GetMaintenanceStateAsync(ct).ConfigureAwait(false);

            var now    = _timeProvider.GetUtcNow();
            var action = Decide(totalRows, vectorIndexRows, _options, _lastRetrain, now);

            switch (action) {
                case KontextMaintenanceAction.EnsureVectorIndex: {
                    var current = await _schema.EnsureVectorIndexAsync(ct).ConfigureAwait(false);

                    // A creation IS a full train: when the index was missing and now exists, start
                    // its retrain clock here so the first time-based rebuild falls one interval
                    // after creation instead of immediately following it.
                    if (vectorIndexRows is null && current)
                        _lastRetrain = now;

                    break;
                }

                case KontextMaintenanceAction.RetrainVectorIndex:
                    await _schema.RetrainVectorIndexAsync(ct).ConfigureAwait(false);

                    // Advance only after the rebuild landed — a failed retrain must stay due.
                    _lastRetrain = now;
                    break;
            }

            // Compaction and vacuum are cadence-independent, always-on hygiene — they run on
            // every tick regardless of what the index needed.
            await _schema.CompactAsync(ct).ConfigureAwait(false);
            await _schema.VacuumAsync(_options.VacuumRetention, _options.VacuumRetainVersions, ct).ConfigureAwait(false);
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

/// <summary>The maintenance <see cref="KontextMaintenanceScheduler.Decide"/> picks for the vector index on one tick.</summary>
public enum KontextMaintenanceAction {
    /// <summary>The vector index needs nothing on this tick.</summary>
    None,

    /// <summary>Run <see cref="KontextSchema.EnsureVectorIndexAsync"/>: create the missing index, or fold the unindexed tail into the existing one.</summary>
    EnsureVectorIndex,

    /// <summary>Run <see cref="KontextSchema.RetrainVectorIndexAsync"/>: the time-based full rebuild is due.</summary>
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

    /// <summary>
    /// The VACUUM LANCE older_than window. Default is 14 days. Must stay comfortably longer than
    /// any plausible connection lifetime: an aggressive vacuum breaks the cached dataset views
    /// open connections still hold (the stale-handle failure the pool then has to recycle).
    /// </summary>
    public TimeSpan VacuumRetention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The VACUUM LANCE retain_n_versions count. Default is 3.</summary>
    public int VacuumRetainVersions { get; set; } = 3;

    /// <summary>The optional logger factory for tick diagnostics; when null, nothing is logged.</summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
