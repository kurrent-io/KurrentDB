// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// The engine's full composition: the execution surface, the journal, the stream, and the
/// knobs. A mutable settings class by design — config binding does not cope with records.
/// <see cref="EnsureValid"/> owns the invariants; the engine calls it at construction, and a
/// host can call it earlier to fail at configuration time instead.
/// </summary>
public class MigrationEngineOptions<TContext> where TContext : class {
    /// <summary>The store's execution surface, handed to every task per call. Required.</summary>
    public TContext? Context { get; set; }

    /// <summary>Where the history lives. Required.</summary>
    public IMigrationJournal? Journal { get; set; }

    /// <summary>The migration stream. Order is irrelevant here — <see cref="IMigrationStep{TContext}.Version"/> is the order.</summary>
    public List<IMigrationStep<TContext>> Steps { get; set; } = [];

    /// <summary>
    /// The explicit reset-from-scratch command: the engine resets the store and replays the
    /// full stream from the beginning. Setting it IS the authorization; there is no second gate.
    /// </summary>
    public bool ForceReset { get; set; }

    /// <summary>The plan renders at Debug; each executed step reports at Information with its duration.</summary>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <summary>Throws when the composition cannot run: no context, no journal, versions below 1,
    /// or duplicate versions. Derived options extend it with their own checks — call the base.</summary>
    public virtual void EnsureValid() {
        if (Context is null)
            throw new InvalidOperationException($"{nameof(MigrationEngineOptions<TContext>)}.{nameof(Context)} is required.");

        if (Journal is null)
            throw new InvalidOperationException($"{nameof(MigrationEngineOptions<TContext>)}.{nameof(Journal)} is required.");

        foreach (var step in Steps)
            if (step.Version < 1)
                throw new InvalidOperationException($"Migration step '{step.Name}' has version {step.Version}; versions start at 1.");

        var duplicates = Steps
            .GroupBy(static step => step.Version)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"version {group.Key} claimed by {string.Join(" and ", group.Select(static step => $"'{step.Name}'"))}")
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Migration step versions are not unique: {string.Join("; ", duplicates)}.");
    }
}
