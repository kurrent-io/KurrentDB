// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;
using Kurrent.Quack;
using Polly;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Kontext's data sources. The engine needs no file of its own — everything durable lives in
/// Lance — so both are in-memory engines that reach their data through attachments.
/// </summary>
public sealed class KontextDataSource : IDuckLanceSchemaExecutor, IDisposable {
    public const string LanceAlias  = "ldb";
    public const string SharedAlias = "kdb";

    const string LanceExtension = "lance";
    const string LanceFilename  = $"{LanceExtension}.duckdb_extension";

    /// <summary>Where <c>stem</c> lives — <see cref="FoldMacro"/> folds through it, so every connection needs it.</summary>
    const string FtsExtension = "fts";
    const string FtsFilename  = $"{FtsExtension}.duckdb_extension";

    /// <summary>Normalizes text for matching: lowercase, strip punctuation, drop determiners, stem each word.</summary>
    public const string FoldMacro =
        """
        CREATE TEMP MACRO fold(t) AS array_to_string(list_transform(list_filter(
            regexp_extract_all(lower(t), '[\p{L}\p{N}]+'),
            lambda word: word NOT IN (
                'the','a','an','my','your','his','her','its','our','their',
                'this','that','these','those','s')),
            lambda word: stem(word, 'english')), ' ');
        """;
    
    static readonly ResiliencePipeline StaleHandleRecycle;

    static KontextDataSource() {
        // Recycles a poisoned connection ONCE: a stale cached dataset view never converges on the same
        // connection, so the retry re-runs the whole callback on a fresh one. No delay — the fresh
        // connection either sees the dataset or the failure is not transient.
        StaleHandleRecycle = new ResiliencePipelineBuilder()
            .AddRetry(new() {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(static ex => {
                    // A dead cached dataset view. Only a fresh connection (re-ATTACH) converges.
                    ReadOnlySpan<char> text = ex.ToString();
                    return (text.Contains("LanceError(IO)", StringComparison.Ordinal) && text.Contains("Not found", StringComparison.Ordinal))
                        || text.Contains("belongs to non-existent fragment", StringComparison.Ordinal);
                }),
                MaxRetryAttempts = 1,
                Delay            = TimeSpan.Zero,
            })
            .Build();
    }

    /// <summary>Kontext's own store, over the Lance namespace.</summary>
    public DuckDBDataSource Local { get; }

    /// <summary>
    /// The same, plus the node's database attached read-only. Reads only: a transaction writing
    /// Lance cannot touch a second attached catalog.
    /// </summary>
    public DuckDBDataSource Shared { get; }

    public KontextDataSource(string storagePath, string tempDirectory, string sharedDatabasePath) {
        ArgumentException.ThrowIfNullOrEmpty(storagePath);
        ArgumentException.ThrowIfNullOrEmpty(tempDirectory);
        ArgumentException.ThrowIfNullOrEmpty(sharedDatabasePath);

        var lanceNamespace = Path.GetFullPath(storagePath);

        // ATTACH does not create the namespace directory. Pure filesystem work, so construction
        // stays free of DB I/O — engine problems surface on the first operation, not here.
        Directory.CreateDirectory(lanceNamespace);

        Local  = DuckDBDataSource.Create(Compose(lanceNamespace, tempDirectory, sharedDatabasePath: ""));
        Shared = DuckDBDataSource.Create(Compose(lanceNamespace, tempDirectory, sharedDatabasePath));
    }

    /// <summary>
    /// A dedicated connection for writing. Never pooled: writers hold one for prepared-statement
    /// reuse and transaction control, and carry the Lance commit-conflict retry themselves.
    /// </summary>
    public DuckDBAdvancedConnection OpenLanceWriter() => Local.OpenDedicatedConnection().Connection;

    public T Execute<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.Execute(
            static (state, token) => {
                token.ThrowIfCancellationRequested();
                using var lease = state.source.OpenDedicatedConnection();
                return state.operation(lease.Connection);
            },
            (source: Local, operation),
            cancellationToken);

    public void Execute(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.Execute(
            static (state, token) => {
                token.ThrowIfCancellationRequested();
                using var lease = state.source.OpenDedicatedConnection();
                state.operation(lease.Connection);
            },
            (source: Local, operation),
            cancellationToken);

    public T ExecuteShared<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.Execute(
            static (state, token) => {
                token.ThrowIfCancellationRequested();
                using var lease = state.source.OpenDedicatedConnection();
                return state.operation(lease.Connection);
            },
            (source: Shared, operation),
            cancellationToken);

    public void ExecuteShared(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.Execute(
            static (state, token) => {
                token.ThrowIfCancellationRequested();
                using var lease = state.source.OpenDedicatedConnection();
                state.operation(lease.Connection);
            },
            (source: Shared, operation),
            cancellationToken);

    public ValueTask<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.ExecuteAsync(
            static (state, token) => new ValueTask<T>(Task.Run(
                () => {
                    using var lease = state.source.OpenDedicatedConnection();
                    return state.operation(lease.Connection);
                },
                token)),
            (source: Local, operation),
            cancellationToken);

    public ValueTask ExecuteAsync(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.ExecuteAsync(
            static (state, token) => new ValueTask(Task.Run(
                () => {
                    using var lease = state.source.OpenDedicatedConnection();
                    state.operation(lease.Connection);
                },
                token)),
            (source: Local, operation),
            cancellationToken);

    public ValueTask<T> ExecuteSharedAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.ExecuteAsync(
            static (state, token) => new ValueTask<T>(Task.Run(
                () => {
                    using var lease = state.source.OpenDedicatedConnection();
                    return state.operation(lease.Connection);
                },
                token)),
            (source: Shared, operation),
            cancellationToken);

    public ValueTask ExecuteSharedAsync(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default) =>
        StaleHandleRecycle.ExecuteAsync(
            static (state, token) => new ValueTask(Task.Run(
                () => {
                    using var lease = state.source.OpenDedicatedConnection();
                    state.operation(lease.Connection);
                },
                token)),
            (source: Shared, operation),
            cancellationToken);

    static DuckDBDataSourceOptions Compose(string storagePath, string tempDirectory, string sharedDatabasePath) {
        var options = new DuckDBDataSourceOptions()
            .ConnectToMemory()
            .Extensions(extensions => {
                // The vendored build is unsigned.
                extensions.AllowUnsigned = true;

                // Shipped beside the application: load it in place. Otherwise let the engine
                // install the stock build from its own repository — which is NOT the vendored
                // one, and does not carry the prefilter fix the vendored build exists for.
                if (DuckDBVendorExtensions.TryGetAppExtensionPath(LanceFilename, out var lancePath))
                    extensions.LoadFrom(lancePath);
                else
                    extensions.Install(LanceExtension);

                // Stock build, so the vendored copy is only about reaching it offline. Declared
                // rather than left to autoload: a missing stemmer should fail the data source, not
                // the first query that folds an alias.
                if (DuckDBVendorExtensions.TryGetAppExtensionPath(FtsFilename, out var ftsPath))
                    extensions.LoadFrom(ftsPath);
                else
                    extensions.Install(FtsExtension);
            })
            .AttachDatabase($"lance:{storagePath}", LanceAlias)
            .UseInitializer(static connection => {
                using var command = connection.CreateCommand();
                command.CommandText = $"USE {LanceAlias}; {FoldMacro}";
                command.ExecuteNonQuery();
            });

        options.Settings["memory_limit"]   = $"{MemoryLimitMib()}MB";
        options.Settings["temp_directory"] = tempDirectory;

        if (sharedDatabasePath.Length > 0)
            options.AttachDatabase(sharedDatabasePath, SharedAlias, attach => attach.ReadOnly());

        return options;
    }

    // The node's own pool already claims a quarter of total RAM for its database, and these are
    // separate engine instances, so this budget is additional rather than a share of that one.
    static int MemoryLimitMib() =>
        (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 * 0.10);

    public void Dispose() {
        Local.Dispose();
        Shared.Dispose();
    }
}
