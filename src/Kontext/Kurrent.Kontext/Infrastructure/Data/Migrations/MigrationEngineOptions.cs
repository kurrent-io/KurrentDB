// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// The engine's full composition: the execution surface, the journal, the two streams, and the
/// knobs. A mutable settings class by design — config binding does not cope with records.
/// <see cref="EnsureValid"/> owns the invariants; the engine calls it at construction, and a
/// host can call it earlier to fail at configuration time instead.
/// </summary>
public class MigrationEngineOptions<TContext> where TContext : class {
    /// <summary>
    /// The store's execution surface, handed to every migration per call. Required.
    /// </summary>
    public TContext Context { get; set; } = null!;

    /// <summary>
    /// Where the history lives. Required.
    /// </summary>
    public IMigrationJournal Journal { get; set; } = null!;

    /// <summary>
    /// The versioned stream. Registration order assigns the versions, so the order here IS the order.
    /// </summary>
    public VersionedMigrations<TContext> Versioned { get; set; } = new();

    /// <summary>
    /// The migrations reasserted on every boot, after the versioned stream. Order here IS the order.
    /// </summary>
    public RepeatableMigrations<TContext> Repeatable { get; set; } = new();

    /// <summary>
    /// The explicit reset-from-scratch command: the engine resets the store and replays the
    /// full stream from the beginning. Setting it IS the authorization; there is no second gate.
    /// </summary>
    public bool ForceReset { get; set; }

    /// <summary>
    /// The engine's logger. Defaults to <see cref="NullLogger.Instance"/>.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    public void ConfigureVersioned(Action<VersionedMigrations<TContext>> configure) =>
        configure(Versioned);

    public void ConfigureRepeatable(Action<RepeatableMigrations<TContext>> configure) =>
        configure(Repeatable);

    public virtual void EnsureValid() {
        if (Context is null)
            throw new InvalidOperationException($"{nameof(MigrationEngineOptions<>)}.{nameof(Context)} is required.");

        if (Journal is null)
            throw new InvalidOperationException($"{nameof(MigrationEngineOptions<>)}.{nameof(Journal)} is required.");

        if (Versioned.Count == 0 && Repeatable.Count == 0)
            throw new InvalidOperationException("No migrations or repeatable migrations were enqueued.");
    }
}

/// <summary>
/// A migration in the versioned stream, with the identity the collection gave it: the version from
/// its registration position, and the key composed from both.
/// </summary>
public readonly record struct PlannedMigration<TContext>(uint Version, string Key, IMigration<TContext> Migration)
    where TContext : class;

/// <summary>
/// A migration in the repeatable set, with the failure policy chosen at registration.
/// </summary>
public readonly record struct RepeatableMigration<TContext>(IMigration<TContext> Migration, bool ContinueOnFailure)
    where TContext : class;

/// <summary>
/// The versioned stream. The author supplies only a name; the version comes from the registration
/// position and the key from both, so a version can never be typed, duplicated, or skipped. Append
/// at the bottom — inserting or removing anything shifts every version after it, which the engine
/// detects on the next boot by comparing recorded keys against the ones the code now declares.
/// </summary>
public class VersionedMigrations<TContext> : IEnumerable<PlannedMigration<TContext>> where TContext : class {
    readonly List<PlannedMigration<TContext>> _steps = [];

    public void Enqueue(IMigration<TContext> migration) {
        if (_steps.Any(x => x.Migration.Name == migration.Name))
            throw new InvalidOperationException($"Migration '{migration.Name}' is already enqueued.");

        var version = (uint)_steps.Count + 1;

        _steps.Add(new(version, MigrationKey.From(version, migration.Name), migration));
    }
    
    public void Enqueue<T>() where T : IMigration<TContext>, new() =>
        Enqueue(new T());

    public void Enqueue(ExecuteAsyncMigration<TContext> execute, string name) =>
        Enqueue(new MigrationProxy<TContext>(name, execute));

    public void Enqueue(ExecuteSyncMigration<TContext> execute, string name) =>
        Enqueue((ctx, ct) => {
            execute(ctx, ct);
            return ValueTask.CompletedTask;
        }, name);

    public uint LastVersion => (uint)_steps.Count;

    public int Count => _steps.Count;

    public void Clear() => _steps.Clear();

    public IEnumerator<PlannedMigration<TContext>> GetEnumerator() => _steps.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public PlannedMigration<TContext> this[Index index] => _steps[index];
}

/// <summary>
/// The repeatable migrations: no version, no journal entry, no history. Each body states the current
/// desired form and the engine reasserts it on every boot, after the versioned stream. Registration
/// order is execution order — a repeatable migration has a sequence, not an identity.
/// </summary>
public class RepeatableMigrations<TContext> : IEnumerable<RepeatableMigration<TContext>> where TContext : class {
    readonly List<RepeatableMigration<TContext>> _steps = [];

    /// <param name="continueOnFailure">
    /// Whether a failure is survivable. False, the default, aborts the boot like any migration
    /// failure. True logs at Warning and lets the rest run — for a body whose failure mode is
    /// transient and whose next boot simply reasserts it again.
    /// </param>
    public void Enqueue(IMigration<TContext> migration, bool continueOnFailure = false) =>
        _steps.Add(new(migration, continueOnFailure));

    public void Enqueue<T>() where T : IMigration<TContext>, new() =>
        Enqueue(new T());
    
    public void Enqueue(ExecuteAsyncMigration<TContext> execute, string name, bool continueOnFailure = false) =>
        Enqueue(new MigrationProxy<TContext>(name, execute), continueOnFailure);

    public void Enqueue(ExecuteSyncMigration<TContext> execute, string name, bool continueOnFailure = false) =>
        Enqueue((ctx, ct) => {
            execute(ctx, ct);
            return ValueTask.CompletedTask;
        }, name, continueOnFailure);

    public int Count => _steps.Count;

    public void Clear() => _steps.Clear();

    public IEnumerator<RepeatableMigration<TContext>> GetEnumerator() => _steps.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public RepeatableMigration<TContext> this[Index index] => _steps[index];
}
