// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.RegularExpressions;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// One change to the store — a class per migration, a file per class. A migration carries a name
/// and nothing else; the collection it is registered in decides how it runs. Registered in
/// <see cref="VersionedMigrations{TContext}"/> it joins the versioned stream, gets a version from its
/// position and a key from both, and its body is FROZEN the day it ships — never edit an old one,
/// append a new one. Registered in <see cref="RepeatableMigrations{TContext}"/> it carries no version,
/// is never recorded, and its body states the CURRENT desired form for every boot to reassert.
/// </summary>
public interface IMigration<in TContext> where TContext : class {
    /// <summary>
    /// The kebab-case name, without a version — "add-timestamp-to-errors-table". In the versioned
    /// stream it is the half of the journal key the author owns, so changing it on a migration that
    /// already ran reads as a reordered stream and the engine refuses to continue.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The change itself, run against the store's execution surface.
    /// </summary>
    ValueTask ExecuteAsync(TContext ctx, CancellationToken ct = default);
}

public abstract class Migration<TContext> : IMigration<TContext> where TContext : class {
    protected Migration(string? name = null) => Name = MigrationKey.From(GetType(), name); // we can just do this: MigrationKey.From(name ?? GetType())

    public string Name { get; }

    public abstract ValueTask ExecuteAsync(TContext ctx, CancellationToken ct = default);
}

/// <summary>
/// The journal key: a zero-padded version welded to the migration's name, "00003-add-embedding-column".
/// The padding is for reading and sorting the journal by eye; the engine orders on the version it
/// assigned, so the width never affects execution.
/// </summary>
public static partial class MigrationKey {
    public const int VersionDigits = 5;

    public static string From(uint version, string name) => $"{version.ToString($"D{VersionDigits}")}-{name}";

    /// <summary>
    /// The explicit name when given, otherwise the type name kebab-cased with a trailing "Task" or
    /// "Step" dropped: AddTimestampToErrorsTask becomes "add-timestamp-to-errors".
    /// </summary>
    public static string From(Type migrationType, string? name) {
        if (name is null) {
            var typeName = migrationType.Name;

            if (typeName.EndsWith("Task", StringComparison.OrdinalIgnoreCase) || typeName.EndsWith("Step", StringComparison.OrdinalIgnoreCase))
                typeName = typeName[..^4];

            name = ToKebabCase(typeName);
        }

        if (NamePattern.IsMatch(name))
            return name;
        
        throw new ArgumentException(
            $"Migration name '{name}' must be kebab-case: lowercase letters and digits, single dashes between words.",
            nameof(name));
    }

    static string ToKebabCase(string value) {
        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++) {
            var c = value[i];

            // The dash goes before an uppercase run that starts a word, not before every uppercase
            // letter, so "DuckDBTable" becomes "duck-db-table" instead of "duck-d-b-table".
            var startsWord = char.IsUpper(c) && i > 0 &&
                (!char.IsUpper(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1])));

            if (startsWord)
                builder.Append('-');

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex NamePattern { get; }
}

class MigrationProxy<TContext>(string? name, ExecuteAsyncMigration<TContext> execute)
    : Migration<TContext>(name) where TContext : class {
    public override ValueTask ExecuteAsync(TContext ctx, CancellationToken ct = default) => execute(ctx, ct);
}

public delegate ValueTask ExecuteAsyncMigration<in TContext>(TContext ctx, CancellationToken ct = default) where TContext : class;

public delegate void ExecuteSyncMigration<in TContext>(TContext ctx, CancellationToken ct = default) where TContext : class;
