// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Modules.Sessions;

/// <summary>
/// The agent session import's background loop: on a fixed cadence it runs the importer's one
/// idempotent DML batch, copying the new tail of Claude Code session data into <c>transcripts</c>
/// (and refreshing the <c>transcript_parse_errors</c> snapshot).
///
/// The split mirrors the rest of the data layer:
/// - <see cref="AgentSessionImporter"/> owns every statement — this class issues no SQL
/// - this class owns the clock, the tick cadence, and the non-overlap gate
///
/// A tick quietly skips while the transcript tables have not been created yet (a tick that fires
/// before the host ran <see cref="AgentSessionImporter.CreateAsync"/>), exactly like
/// <see cref="KontextMaintenanceScheduler"/> skips before its schema exists. Otherwise every tick
/// just imports — there is no per-tick Decide step. A tick never throws: every failure is logged and
/// the next tick simply tries again — which is also the offline story, since the first INSTALL of
/// the community extension needs network.
/// </summary>
public sealed class AgentSessionImportScheduler : IDisposable {
    readonly AgentSessionImporter      _importer;
    readonly ILogger                   _logger;
    readonly AgentSessionImportOptions _options;
    readonly TimeProvider              _timeProvider;
    readonly ITimer                    _timer;

    // Interlocked disposal flag: 0 = live, 1 = disposed.
    int _disposed;

    // Interlocked non-overlap gate: 0 = idle, 1 = a tick is running.
    int _tickGate;

    public AgentSessionImportScheduler(AgentSessionImporter importer, AgentSessionImportOptions options, TimeProvider? timeProvider = null) {
        _importer = importer;
        _options  = options;

        // Tests supply a fake TimeProvider for deterministic clocks and timers.
        _timeProvider = timeProvider ?? TimeProvider.System;

        _logger = options.LoggerFactory?.CreateLogger(typeof(AgentSessionImportScheduler)) ?? NullLogger.Instance;

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

    // The single tick body. Never throws — every failure is caught and logged; quietly skips while
    // the transcript tables have not been created yet (a tick that fires before the host ran
    // AgentSessionImporter.CreateAsync). The next tick retries whatever this one left undone (an
    // offline tick whose INSTALL could not reach the community feed is exactly this case).
    async Task RunTickBodyAsync(CancellationToken ct) {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try {
            if (!await _importer.ExistsAsync(ct).ConfigureAwait(false)) {
                _logger.LogDebug("Agent session import tick skipped: the transcript tables do not exist yet.");
                return;
            }

            await _importer.ImportAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("Agent session import tick imported the new session tail.");
        } catch (Exception ex) {
            // A background import tick must never surface an exception onto the timer thread;
            // the next tick retries the whole idempotent batch.
            _logger.LogError(ex, "Agent session import tick failed.");
        }
    }
}

/// <summary>
/// The import loop's knobs. A mutable settings class by design — config binding does not cope with
/// records.
/// </summary>
public sealed class AgentSessionImportOptions {
    /// <summary>
    /// The Claude Code data root scanned by the importer. Default is <c>&lt;user profile&gt;/.claude</c>,
    /// resolved to an absolute path in .NET — never relying on duckdb '~' expansion, because the
    /// server process HOME is not the agent's.
    /// </summary>
    public string SourcePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>The background tick period. Default is 5 minutes.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The optional logger factory for tick diagnostics; when null, nothing is logged.</summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
