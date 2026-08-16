// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// Executes the migration stream: reads the history, plans the steps above the current
/// version, and runs them in order — recording what ran, when, and how long it took.
/// That is the whole contract. What a step does, and whether it is safe, is the step
/// author's responsibility.
///
/// <see cref="MigrationEngineOptions{TContext}.ForceReset"/> is the one extra: reset the
/// store via <see cref="ResetAsync"/> and replay the stream from the beginning; the store
/// re-derives from scratch.
/// </summary>
[PublicAPI]
public abstract partial class MigrationEngine<TContext> where TContext : class {
    protected readonly TContext                         Context;
    protected readonly IMigrationJournal                Journal;
    protected readonly ILogger                          Logger;
    protected readonly MigrationEngineOptions<TContext> Options;
    protected readonly IMigrationStep<TContext>[]       Steps;

    protected MigrationEngine(MigrationEngineOptions<TContext> options) {
        options.EnsureValid();
        
        Options = options;
        Steps   = [.. options.Steps.OrderBy(static step => step.Version)];
        Context = options.Context!;
        Journal = options.Journal!;
        Logger  = options.LoggerFactory.CreateLogger<MigrationEngine<TContext>>();
    }

    /// <summary>Runs the pending part of the stream. Call ONCE at host bootstrap, before anything queries the store.</summary>
    public virtual async Task EnsureAsync(CancellationToken ct = default) {
        if (Options.ForceReset) {
            LogReset();

            await ResetAsync(Context, ct).ConfigureAwait(false);
        }

        await Journal.EnsureAsync(ct).ConfigureAwait(false);

        var current = await Journal.LoadCurrentVersionAsync(ct).ConfigureAwait(false);

        if (Steps.Length > 0 && current > Steps[^1].Version)
            throw new InvalidOperationException(
                $"The store is at migration version {current} but the stream ends at {Steps[^1].Version}. " +
                "Downgrades are not supported; the running code is older than the store.");

        // RunOnce steps above the recorded version, plus every RunAlways step — merged in
        // version order, so a reasserted view still lands after the tables it reads.
        var plan = Steps.Where(step => step.Type is MigrationStepType.RunAlways || step.Version > current).ToList();

        if (plan.Count == 0) {
            LogUpToDate(current);
            return;
        }

        LogMigrationStarting(current, plan[^1].Version, plan.Count);

        foreach (var step in plan)
            LogPlannedStep(step.Version, step.Name, step.Type);

        foreach (var step in plan) {
            LogExecutingMigration(step.Version, step.Name);

            var started = Stopwatch.GetTimestamp();

            await step.ExecuteAsync(Context, ct).ConfigureAwait(false);

            var duration = Stopwatch.GetElapsedTime(started);

            await Journal.RecordAsync(new(step.Version, step.Name, duration), ct).ConfigureAwait(false);

            LogMigrationCompleted(step.Version, step.Name, Math.Round(duration.TotalMilliseconds, 1));
        }
    }

    /// <summary>
    /// What reset-from-scratch means for THIS store, ahead of the full replay. Versionless
    /// and unjournaled on purpose — it runs outside the stream and destroys the history the
    /// stream is measured against. A store where reset is nonsensical throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    protected abstract Task ResetAsync(TContext context, CancellationToken ct);

    [LoggerMessage(LogLevel.Information, "[migrate] FORCE RESET: store torn down, stream replays from zero")]
    partial void LogReset();

    [LoggerMessage(LogLevel.Debug, "[migrate] up to date at v{Version}")]
    partial void LogUpToDate(int version);

    [LoggerMessage(LogLevel.Information, "[migrate] v{CurrentVersion} -> v{TargetVersion}, {StepCount} step(s) pending")]
    partial void LogMigrationStarting(int currentVersion, int targetVersion, int stepCount);

    [LoggerMessage(LogLevel.Debug, "[migrate] plan v{Version} {Name} ({Type})")]
    partial void LogPlannedStep(int version, string name, MigrationStepType type);

    [LoggerMessage(LogLevel.Information, "[migrate] exec v{Version} {Name}")]
    partial void LogExecutingMigration(int version, string name);

    [LoggerMessage(LogLevel.Information, "[migrate] done v{Version} {Name} ({ElapsedMs}ms)")]
    partial void LogMigrationCompleted(int version, string name, double elapsedMs);
}
