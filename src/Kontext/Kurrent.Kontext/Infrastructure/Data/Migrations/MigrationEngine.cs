// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// Executes the migration stream: reads the history, checks it still matches the code, plans the
/// migrations above the current version, and runs them in order — recording which key ran and how
/// long it took. Then it runs the repeatable migrations, which carry no version and are never
/// recorded. That is the whole contract. What a migration does, and whether it is safe, is the
/// author's responsibility.
///
/// <see cref="MigrationEngineOptions{TContext}.ForceReset"/> is the one extra: reset the
/// store via <see cref="ResetAsync"/> and replay the stream from the beginning; the store
/// re-derives from scratch.
/// </summary>
[PublicAPI]
public abstract partial class MigrationEngine<TContext> where TContext : class {
    protected readonly MigrationEngineOptions<TContext> Options;
    protected readonly ILogger                          Logger;

    protected MigrationEngine(MigrationEngineOptions<TContext> options) {
        options.EnsureValid();
        Options = options;
        Logger  = options.Logger;
    }

    protected TContext                  Context    => Options.Context;
    protected IMigrationJournal         Journal    => Options.Journal;
    protected bool                      ForceReset => Options.ForceReset;
    protected VersionedMigrations<TContext>  Steps      => Options.Versioned;
    protected RepeatableMigrations<TContext> Repeatable => Options.Repeatable;

    /// <summary>
    /// Runs the pending part of the stream. Call ONCE at host bootstrap, before anything queries the store.
    /// </summary>
    public virtual async ValueTask EnsureAsync(CancellationToken ct = default) {
        if (ForceReset) {
            LogReset();
            await ResetAsync(Context, ct).ConfigureAwait(false);
        }

        await Journal
            .EnsureAsync(ct)
            .ConfigureAwait(false);

        var executed = await Journal.ListAsync(ct).ConfigureAwait(false);
        var current  = Reconcile(executed);

        var plan = Steps.Where(step => step.Version > current).ToList();

        if (plan.Count == 0)
            LogUpToDate(current);
        else
            await RunStreamAsync(plan, current, ct).ConfigureAwait(false);

        await RunRepeatableAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the recorded history against the stream the code declares, and returns the store's
    /// current version. Versions come from registration position, so inserting, removing, renaming,
    /// or reordering a migration silently renumbers everything after it — the recorded key is what
    /// catches that, before a single statement runs.
    /// </summary>
    uint Reconcile(IReadOnlyList<ExecutedMigration> executed) {
        if (executed.Count == 0)
            return 0;

        var current = executed.Max(entry => entry.Version);

        if (current > Steps.LastVersion)
            throw new InvalidOperationException(
                $"The store is at migration version {current} but the stream ends at {Steps.LastVersion}. " +
                "Downgrades are not supported; the running code is older than the store.");

        foreach (var entry in executed) {
            var planned = Steps[(int)entry.Version - 1];

            if (planned.Key != entry.Key)
                throw new InvalidOperationException(
                    $"Migration v{entry.Version} ran as '{entry.Key}' but the code declares '{planned.Key}'. " +
                    "The stream was reordered: a migration was inserted, removed, or renamed. Restore it " +
                    "in place — the stream is append-only.");
        }

        return current;
    }

    async ValueTask RunStreamAsync(List<PlannedMigration<TContext>> plan, uint current, CancellationToken ct) {
        LogMigrationStarting(current, plan[^1].Version, plan.Count);

        foreach (var step in plan)
            LogPlannedMigration(step.Key);

        foreach (var step in plan) {
            LogExecutingMigration(step.Key);

            var started = TimeProvider.System.GetTimestamp();

            await step.Migration
                .ExecuteAsync(Context, ct)
                .ConfigureAwait(false);

            var duration = TimeProvider.System.GetElapsedTime(started);

            await Journal
                .RecordAsync(new(step.Version, step.Key, "", duration), ct)
                .ConfigureAwait(false);

            LogMigrationCompleted(step.Key, Math.Round(duration.TotalMilliseconds, 1));
        }
    }

    // Runs after the stream, so a reasserted form always lands on the shape the stream just built.
    // Nothing is journaled: a repeatable migration has no version to record, and recording one would
    // make removing it look like a downgrade on the next boot.
    async ValueTask RunRepeatableAsync(CancellationToken ct) {
        foreach (var (migration, continueOnFailure) in Repeatable) {
            LogExecutingRepeatable(migration.Name);

            var started = TimeProvider.System.GetTimestamp();

            try {
                await migration.ExecuteAsync(Context, ct).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException && continueOnFailure) {
                LogRepeatableFailed(ex, migration.Name);
                continue;
            }

            LogRepeatableCompleted(migration.Name, Math.Round(TimeProvider.System.GetElapsedTime(started).TotalMilliseconds, 1));
        }
    }

    /// <summary>
    /// What reset-from-scratch means for THIS store, ahead of the full replay. Versionless
    /// and unjournaled on purpose — it runs outside the stream and destroys the history the
    /// stream is measured against. A store where reset is nonsensical throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    protected abstract ValueTask ResetAsync(TContext ctx, CancellationToken ct);

    [LoggerMessage(LogLevel.Information, "[migrate] FORCE RESET: store torn down, stream replays from zero")]
    partial void LogReset();

    [LoggerMessage(LogLevel.Debug, "[migrate] up to date at v{Version}")]
    partial void LogUpToDate(uint version);

    [LoggerMessage(LogLevel.Information, "[migrate] v{CurrentVersion} -> v{TargetVersion}, {StepCount} pending")]
    partial void LogMigrationStarting(uint currentVersion, uint targetVersion, int stepCount);

    [LoggerMessage(LogLevel.Debug, "[migrate] plan {Key}")]
    partial void LogPlannedMigration(string key);

    [LoggerMessage(LogLevel.Information, "[migrate] exec {Key}")]
    partial void LogExecutingMigration(string key);

    [LoggerMessage(LogLevel.Information, "[migrate] done {Key} ({ElapsedMs}ms)")]
    partial void LogMigrationCompleted(string key, double elapsedMs);

    [LoggerMessage(LogLevel.Information, "[migrate] exec repeatable {Name}")]
    partial void LogExecutingRepeatable(string name);

    [LoggerMessage(LogLevel.Information, "[migrate] done repeatable {Name} ({ElapsedMs}ms)")]
    partial void LogRepeatableCompleted(string name, double elapsedMs);

    [LoggerMessage(LogLevel.Warning, "[migrate] repeatable {Name} failed; continuing")]
    partial void LogRepeatableFailed(Exception ex, string name);
}
