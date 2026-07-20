using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Class for accessing the list of collections in a DuckDB + Lance vector store.
/// </summary>
/// <remarks>
/// This class can be used with collections of any schema type, but requires you to provide schema information when getting a collection.
/// </remarks>
public sealed class DuckDBVectorStore : VectorStore {
    // The system name reported in metadata and exceptions for this provider.
    const string VectorStoreSystemName = "ducklance";

    // A general purpose definition used to construct a throwaway collection for schema-agnostic proxy operations.
    static readonly VectorStoreCollectionDefinition GeneralPurposeDefinition = new() { Properties = [new VectorStoreKeyProperty("Key", typeof(string))] };

    readonly VectorStoreMetadata _metadata;

    // The store options, copied at construction time so later external mutation of the caller's instance has no effect.
    readonly DuckDBVectorStoreOptions _options;

    // The always-on background reindex schedulers, one per dataset (keyed by the collection's DatasetPath).
    // Registered lazily on the first GetCollection/GetDynamicCollection for a dataset, and disposed together
    // when the store is disposed. The Lazy<T> wrapper ensures exactly one scheduler (and therefore one timer)
    // is created per dataset even under concurrent registration.
    readonly ConcurrentDictionary<string, Lazy<DuckDBReindexScheduler>> _schedulers = new(StringComparer.Ordinal);

    // Tracks whether Dispose(bool) has already run, so the connection manager is only disposed once.
    bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDBVectorStore"/> class.
    /// </summary>
    /// <param name="options">Configuration options for this class.</param>
    /// <exception cref="ArgumentException"><see cref="DuckDBVectorStoreOptions.DatabasePath"/> is empty, or its file name's stem equals the attach alias.</exception>
    public DuckDBVectorStore(DuckDBVectorStoreOptions options) {
        ArgumentException.ThrowIfNullOrEmpty(options.DatabasePath);

        _options = new(options);

        Resolver          = new(_options.DatabasePath);
        ConnectionManager = new(_options, LanceDatasetResolver.DefaultStorageAlias);

        _metadata = new() { VectorStoreSystemName = VectorStoreSystemName, VectorStoreName = Resolver.DatabasePath };
    }

    /// <summary>Gets the connection manager backing this store, for use by collections and tests.</summary>
    internal DuckDBConnectionManager ConnectionManager { get; }

    /// <summary>Gets the dataset resolver backing this store, for use by collections and tests.</summary>
    internal LanceDatasetResolver Resolver { get; }

    /// <summary>Gets the number of background reindex schedulers currently registered (one per dataset). For tests.</summary>
    internal int SchedulerCount => _schedulers.Count;

    /// <inheritdoc />
    [RequiresDynamicCode(
        "This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode(
        "This overload of GetCollection() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    public override DuckDBCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null) {
        if (typeof(TRecord) == typeof(Dictionary<string, object?>))
            throw new ArgumentException(VectorDataStrings.GetCollectionWithDictionaryNotSupported);

        var collection = new DuckDBCollection<TKey, TRecord>(
            ConnectionManager,
            Resolver,
            name,
            modelFactory: () => new DuckDBModelBuilder().Build(
                typeof(TRecord), typeof(TKey), definition,
                _options.EmbeddingGenerator),
            _options);

        EnsureScheduler(collection.DatasetPath, collection.QualifiedTableName, collection.EnsureVectorIndexesAsync);

        return collection;
    }

    /// <inheritdoc />
    public override DuckDBCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition) {
        var collection = CreateDynamicCollection(name, definition);

        EnsureScheduler(collection.DatasetPath, collection.QualifiedTableName, collection.EnsureVectorIndexesAsync);

        return collection;
    }

    /// <summary>Constructs a dynamic collection <em>without</em> registering a reindex scheduler.</summary>
    DuckDBCollection<object, Dictionary<string, object?>> CreateDynamicCollection(string name, VectorStoreCollectionDefinition definition) =>
        // Used by the store's own schema-agnostic proxy operations (CollectionExistsAsync,
        // EnsureCollectionDeletedAsync), which build a throwaway general-purpose collection and must not spin
        // up — or, worse, pollute the registry with a keyless-schema — scheduler for the dataset.
        new(
            ConnectionManager,
            Resolver,
            name,
            modelFactory: () => new DuckDBModelBuilder().BuildDynamic(definition, _options.EmbeddingGenerator),
            _options);

    /// <summary>
    /// Registers (get-or-adds) the always-on background reindex scheduler for a dataset on first request; the
    /// <see cref="Lazy{T}"/> value factory guarantees exactly one scheduler — and one timer — per dataset,
    /// even if several collections for the same dataset are requested concurrently.
    /// </summary>
    void EnsureScheduler(string datasetPath, string qualifiedTableName, Func<CancellationToken, Task<int>> ensureVectorIndexesAsync) =>
        _ = _schedulers.GetOrAdd(
                datasetPath,
                valueFactory: static (path, args) => new(() => new(
                    args.Store.ConnectionManager,
                    path,
                    args.QualifiedTableName,
                    args.EnsureVectorIndexesAsync,
                    args.Store._options.ReindexOptions)),
                (Store: this, QualifiedTableName: qualifiedTableName, EnsureVectorIndexesAsync: ensureVectorIndexesAsync))
            .Value;

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        const string OperationName = "ListCollectionNames";

        var tableNames = await VectorStoreErrorHandler
            .RunOperationAsync<List<string>, DbException>(
                _metadata,
                OperationName,
                operation: () => ConnectionManager.ExecuteAsync(
                    operation: connection => {
                        using var command = connection.CreateCommand();
                        command.CommandText = "SELECT table_name FROM duckdb_tables() WHERE database_name = ?";
                        command.Parameters.Add(new(ConnectionManager.StorageAlias));

                        var       names   = new List<string>();
                        using var reader  = command.ExecuteReader();
                        var       ordinal = reader.GetOrdinal("table_name");

                        while (reader.Read()) {
                            names.Add(reader.GetString(ordinal));
                        }

                        return names;
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        // By convention the table name IS the collection name, so every table in the attachment is a collection.
        foreach (var tableName in tableNames) {
            yield return tableName;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default) {
        using var collection = CreateDynamicCollection(name, GeneralPurposeDefinition);
        return await collection.CollectionExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default) {
        using var collection = CreateDynamicCollection(name, GeneralPurposeDefinition);
        await collection.EnsureCollectionDeletedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the registered reindex scheduler for a dataset path, for deterministic test ticking.</summary>
    internal DuckDBReindexScheduler GetScheduler(string datasetPath) => _schedulers[datasetPath].Value;

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null
            ? null
            : serviceType == typeof(VectorStoreMetadata)
                ? _metadata
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (disposing && !_disposed) {
            // Stop and dispose every background scheduler (their timers) BEFORE the connection manager, so no
            // in-flight tick can run against a disposed connection pool.
            foreach (var scheduler in _schedulers.Values) {
                if (scheduler.IsValueCreated)
                    scheduler.Value.Dispose();
            }

            _schedulers.Clear();

            ConnectionManager.Dispose();
            _disposed = true;
        }

        base.Dispose(disposing);
    }
}