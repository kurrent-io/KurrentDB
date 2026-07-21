using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Service for storing and retrieving vector records, that uses DuckDB + the <c>lance</c> extension as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Can be <see cref="string"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
// #pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class DuckDBCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
    where TKey : notnull
    where TRecord : class {
    // The maximum number of records folded into a single multi-row MERGE statement in the batch upsert path.
    // Larger batches are chunked into statements of at most this many rows, bounding the generated SQL length
    // and the positional-parameter count (rows × property-count) per command.
    const int MaxUpsertBatchRows = 500;

    // Default options reused when a caller passes none, mirroring the SqliteVec idiom.
    static readonly HybridSearchOptions<TRecord> DefaultHybridSearchOptions = new();
    static readonly VectorSearchOptions<TRecord> DefaultVectorSearchOptions = new();

    readonly VectorStoreCollectionMetadata _collectionMetadata;
    readonly DuckDBConnectionManager       _connectionManager;
    readonly RecordCodec<TRecord>          _codec;

    internal DuckDBCollection(
        DuckDBConnectionManager connectionManager,
        LanceDatasetResolver resolver,
        string name,
        Func<CollectionModel> modelFactory,
        DuckDBVectorStoreOptions options
    ) {
        if (typeof(TKey) != typeof(string) && typeof(TKey) != typeof(object))
            throw new NotSupportedException("Only string keys are supported.");

        _connectionManager = connectionManager;

        // Building the model validates the record type (or definition) up front, before any storage
        // resolution happens, so a bad record shape fails fast with the model builder's own exception.
        Model = modelFactory();

        // A registered codec wins; every other record type falls back to the model-driven codec —
        // resolution never fails and never asks.
        _codec = options.Codecs.Resolve<TRecord>() ?? new DuckDBModelCodec<TRecord>(Model);

        Name = name;

        // Resolving the table name validates the collection name against the Lance naming rule at
        // construction time, so an invalid collection name also fails fast.
        TableName          = resolver.GetTableName(name);
        QualifiedTableName = resolver.GetQualifiedTableName(name);
        DatasetPath        = resolver.GetDatasetPath(name);

        _collectionMetadata = new() {
            VectorStoreSystemName = "ducklance",
            VectorStoreName       = resolver.DatabasePath,
            CollectionName        = name
        };
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>Gets the collection's validated model, describing its key, data, and vector properties.</summary>
    internal CollectionModel Model { get; }

    /// <summary>Gets the resolved DuckDB table name for this collection (by convention, the collection name itself).</summary>
    internal string TableName { get; }

    /// <summary>Gets the fully qualified table name for this collection, in the form <c>ldb.main.{TableName}</c>.</summary>
    internal string QualifiedTableName { get; }

    /// <summary>Gets the filesystem path to this collection's raw Lance dataset file.</summary>
    internal string DatasetPath { get; }

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorSearchResult<TRecord>> HybridSearchAsync<TInput>(
        TInput searchValue,
        ICollection<string> keywords,
        int top,
        HybridSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
        where TInput : notnull {
        const string OperationName = "HybridSearch";

        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        // The keyword collection is joined into a single space-separated full-text query string, bound as a
        // positional parameter (never inlined). An empty collection cannot form a query.
        var keywordQuery = string.Join(" ", keywords);

        if (keywordQuery.Length == 0)
            throw new ArgumentException("At least one non-empty keyword is required for a hybrid search.", nameof(keywords));

        options ??= DefaultHybridSearchOptions;

        if (options.IncludeVectors && Model.EmbeddingGenerationRequired)
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);

        // ScoreThreshold is applied client-side, AFTER the top-k rows are fetched: hybrid scores are
        // higher-is-better (_hybrid_score), so a row passes when its score is >= the threshold. A result within
        // Top that fails is dropped, so the returned count may be fewer than Top (MEVD convention).
        var scoreThreshold = options.ScoreThreshold;

        // The vector modality resolves through the same helper SearchAsync uses; the text modality resolves the
        // single IsFullTextIndexed data property (or the one AdditionalProperty selects when several exist).
        var vectorProperty = Model.GetVectorPropertyOrSingle<TRecord>(new() { VectorProperty = options.VectorProperty });

        // Reject an unsupported distance function up front, before any query is issued.
        DuckDBScoreConverter.ValidateDistanceFunction(vectorProperty.DistanceFunction);

        var textProperty = Model.GetFullTextDataPropertyOrSingle(options.AdditionalProperty);

        // Translate the filter once, up front: an unsupported filter throws here — before any SQL is composed or
        // executed — so a filtered hybrid search can never silently degrade to an unfiltered or partial one.
        var filter = options.Filter is null
            ? null
            : new DuckDBFilterTranslator().Translate(options.Filter, Model);

        // Resolve the query vector from TInput exactly as SearchAsync does: direct vector shapes are used as-is;
        // otherwise a configured embedding generator produces the embedding; otherwise the two-way throw
        // distinguishes "no generator configured" from "an incompatible generator configured".
        var vector = searchValue switch {
            ReadOnlyMemory<float> r => r,
            float[] f               => new(f),
            Embedding<float> e      => e.Vector,
            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => ((Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false)).Vector,

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(
                    VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), DuckDBModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(
                    VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

        // PQ-family indexes (and the unindexed brute-force path, which behaves like Dynamic) need a refine pass
        // to keep small-k results accurate; Flat and IVF_FLAT do not. Same rule as SearchAsync.
        var refine = vectorProperty.IndexKind switch {
            null                    => true,
            IndexKind.Dynamic       => true,
            IndexKind.QuantizedFlat => true,
            IndexKind.Hnsw          => true,
            _                       => false
        };

        var skip           = options.Skip;
        var k              = top + skip;
        var includeVectors = options.IncludeVectors;

        var selectColumns = new List<string>(Model.Properties.Count);

        foreach (var property in Model.Properties) {
            if (property is VectorPropertyModel && !includeVectors)
                continue;

            selectColumns.Add(property.StorageName);
        }

        // The unfiltered statement is fully determined here; a filtered statement is composed inside the pooled
        // operation instead, because an oversampling filter must first read the table's row count on the same
        // rented connection before its k can be chosen.
        var unfilteredSql = filter is null
            ? DuckDBSearchSqlComposer.BuildHybridSearchSql(
                QualifiedTableName,
                vectorProperty.StorageName,
                vectorProperty.Dimensions,
                textProperty.StorageName,
                k,
                refine,
                selectColumns,
                null,
                top,
                skip)
            : null;

        var vectorParameter = ToFloatList(vector);
        var vectorColumn    = vectorProperty.StorageName;
        var dimensions      = vectorProperty.Dimensions;
        var textColumn      = textProperty.StorageName;

        // DuckDB has no async reader, so materialize the whole result set eagerly inside the single pooled
        // operation (already ordered by descending _hybrid_score), then yield the materialized results.
        var results = await VectorStoreErrorHandler
            .RunOperationAsync<List<VectorSearchResult<TRecord>>, DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        string sql;

                        if (filter is null)
                            sql = unfilteredSql!;
                        else if (filter.RequiresOversample) {
                            // Containment (array_has_any) is NOT pushed into the extension's prefilter IR — DuckDB
                            // post-filters it over whatever candidates the hybrid search already returned, so
                            // correctness requires k to cover the WHOLE table (same policy, and same root cause, as
                            // SearchAsync's oversample path).
                            long count;

                            using (DbCommand countCommand = connection.CreateCommand()) {
                                countCommand.CommandText = $"SELECT count(*) FROM {QualifiedTableName}";
                                var countResult = countCommand.ExecuteScalar();
                                count = countResult is not null ? Convert.ToInt64(countResult, CultureInfo.InvariantCulture) : 0;
                            }

                            var oversampleK = (int)Math.Max(count, k);

                            sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
                                QualifiedTableName, vectorColumn, dimensions,
                                textColumn, oversampleK, refine,
                                selectColumns, filter.WhereClause, top,
                                skip);
                        } else {
                            // Equality-only filters push down as a TRUE prefilter (prefilter := true), so the normal
                            // k = top + skip is exact even when matching rows fall outside the global top-k.
                            sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
                                QualifiedTableName, vectorColumn, dimensions,
                                textColumn, k, refine,
                                selectColumns, filter.WhereClause, top,
                                skip);
                        }

                        using DbCommand command = connection.CreateCommand();
                        command.CommandText = sql;

                        // Parameters in SQL order: the query vector (first positional, in the FROM clause), then the
                        // keyword query text (second positional, in the FROM clause), then the filter's parameters in
                        // the order their placeholders appear in the WHERE clause.
                        command.Parameters.Add(new DuckDBParameter(vectorParameter));
                        command.Parameters.Add(new DuckDBParameter(keywordQuery));

                        if (filter is not null) {
                            foreach (var filterParameter in filter.Parameters)
                                command.Parameters.Add(new DuckDBParameter(filterParameter ?? DBNull.Value));
                        }

                        var rows = new List<VectorSearchResult<TRecord>>();

                        using var reader = command.ExecuteReader();

                        var hybridScoreOrdinal = reader.GetOrdinal("_hybrid_score");

                        while (reader.Read()) {
                            var record = _codec.Decode(reader, includeVectors);

                            // Lance surfaces _hybrid_score as a single-precision FLOAT, which DuckDB.NET materializes
                            // as a System.Single; GetValue + Convert widens it (and tolerates a DOUBLE column) without
                            // the strict-cast InvalidCastException that reader.GetDouble would throw on a Single.
                            var score = Convert.ToDouble(reader.GetValue(hybridScoreOrdinal), CultureInfo.InvariantCulture);

                            // Post-fetch score-threshold filter: drop this row when its hybrid score is below threshold.
                            if (scoreThreshold is not null && !DuckDBScoreConverter.PassesHybridThreshold(score, scoreThreshold.Value))
                                continue;

                            rows.Add(new(record, score));
                        }

                        return rows;
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        foreach (var result in results)
            yield return result;
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null
            ? null
            : serviceType == typeof(VectorStoreCollectionMetadata)
                ? _collectionMetadata
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default) {
        const string OperationName = "CollectionExists";

        return VectorStoreErrorHandler.RunOperationAsync<bool, DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync(
                operation: connection => TableExists(connection),
                cancellationToken));
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default) {
        const string OperationName = "EnsureCollectionExists";

        // The inner operation returns whether the table already existed, so the caller can decide, outside the
        // rented connection, whether to also chase vector indexes via a separate EnsureVectorIndexesAsync call
        // (its own error-handled operation, on its own rented connection).
        var tableAlreadyExisted = await VectorStoreErrorHandler
            .RunOperationAsync<bool, DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        // Storage names are emitted as UNQUOTED identifiers, so a name that collides with a DuckDB
                        // reserved word (e.g. a column named "when") breaks CREATE TABLE. Quoting is not a viable
                        // escape hatch here: the lance extension's DELETE predicate pushdown silently matches zero
                        // rows for a quoted reserved-word column (probed live for Stage 11), so a reserved-word column
                        // could be created but never reliably deleted. Reject such models up front instead.
                        ValidateNoReservedWordStorageNames(connection, OperationName);

                        var actualColumns = new List<string>();

                        using (DbCommand command = connection.CreateCommand()) {
                            command.CommandText = "SELECT column_name FROM duckdb_columns() WHERE database_name = ? AND table_name = ?";
                            command.Parameters.Add(new DuckDBParameter(_connectionManager.StorageAlias));
                            command.Parameters.Add(new DuckDBParameter(TableName));

                            using var reader  = command.ExecuteReader();
                            var       ordinal = reader.GetOrdinal("column_name");

                            while (reader.Read()) {
                                actualColumns.Add(reader.GetString(ordinal));
                            }
                        }

                        if (actualColumns.Count == 0) {
                            // The table does not exist yet: create it from the model.
                            using DbCommand createCommand = connection.CreateCommand();
                            createCommand.CommandText = DuckDBSchemaBuilder.BuildCreateTableSql(QualifiedTableName, Model);
                            createCommand.ExecuteNonQuery();

                            // Stage 7: index creation (CREATE INDEX ...) is wired here. Scalar indexes (BTREE/
                            // LABEL_LIST/INVERTED) create fine against a freshly created, empty table, so they are
                            // created eagerly, on this same connection, for every data property that requests one.
                            // Vector indexes are NOT created here: the table is empty and Lance cannot train a
                            // vector index against an empty dataset (see EnsureVectorIndexesAsync's remarks), so
                            // those are left for a later, lazy EnsureVectorIndexesAsync call once the collection has
                            // grown enough rows.
                            CreateScalarIndexes(connection, null);

                            return false;
                        }

                        // The table already exists: gate on schema drift. Column types are deliberately not
                        // compared in v1 — this is a name-set comparison only.
                        var expectedColumns = new HashSet<string>(Model.Properties.Select(p => p.StorageName), StringComparer.Ordinal);
                        var actualColumnSet = new HashSet<string>(actualColumns, StringComparer.Ordinal);

                        if (!expectedColumns.SetEquals(actualColumnSet)) {
                            var expected = string.Join(", ", expectedColumns.OrderBy(keySelector: c => c, StringComparer.Ordinal));
                            var actual   = string.Join(", ", actualColumnSet.OrderBy(keySelector: c => c, StringComparer.Ordinal));

                            throw new VectorStoreException(
                                $"Collection '{Name}' already exists with a different schema. Expected columns: [{expected}]; actual columns: [{actual}]. Schema migration is the caller's responsibility.") {
                                VectorStoreSystemName = _collectionMetadata.VectorStoreSystemName,
                                VectorStoreName       = _collectionMetadata.VectorStoreName,
                                CollectionName        = _collectionMetadata.CollectionName,
                                OperationName         = OperationName
                            };
                        }

                        // Stage 7: the table already existed (e.g. re-ensuring a previously created, possibly
                        // populated collection). Only the scalar indexes that are not already present are created
                        // here — CREATE INDEX on an already-existing name errors — so SHOW INDEXES is consulted
                        // first. Vector indexes for this path are handled below, after the connection is released,
                        // via EnsureVectorIndexesAsync.
                        var existingIndexNames = new HashSet<string>(ShowIndexes(connection).Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
                        CreateScalarIndexes(connection, existingIndexNames);

                        return true;
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        if (tableAlreadyExisted) {
            // An already-existing collection may have crossed the 256-row vector-index training floor since it
            // was last ensured (or may never have had a chance to train one at all); give it a chance to pick
            // up its vector index(es) on every re-ensure.
            await EnsureVectorIndexesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lazily creates any missing vector index for the collection's vector properties, once the table holds
    /// at least 256 rows; returns the number of indexes created (<c>0</c> when none were eligible).
    /// </summary>
    internal Task<int> EnsureVectorIndexesAsync(CancellationToken cancellationToken = default) {
        // Vector index creation is deliberately LAZY rather than eager. The DuckDB lance extension version
        // this provider targets refuses to create a PQ-family vector index (IVF_PQ, IVF_HNSW_PQ) — and,
        // empirically, every vector index type — against a dataset with fewer than 256 rows: it always fails
        // with "Creating empty vector indices with train=False is not yet implemented", regardless of any
        // train/training WITH parameter. A "create the index shape now, train it later" design is therefore
        // not viable against this extension version, so index creation is deferred entirely until the
        // 256-row floor is reached. Searching an unindexed vector column falls back to Lance's brute-force
        // scan — slower than an indexed search but exactly as correct — so leaving the index uncreated until
        // then is safe.
        //
        // Called from EnsureCollectionExistsAsync's already-existed (drift-check) path, so re-ensuring an
        // existing, now-populated collection has a chance to pick up its vector index. Also the entry point
        // the reindex scheduler and OptimizeIndexesAsync call, so a collection that crosses the 256-row
        // floor purely through ongoing writes — with no further EnsureCollectionExists call at all — still
        // eventually gets indexed.
        const string OperationName               = "EnsureVectorIndexes";
        const long   VectorIndexTrainingRowFloor = 256;

        return VectorStoreErrorHandler.RunOperationAsync<int, DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync(
                operation: connection => {
                    var indexedFields = new HashSet<string>(ShowIndexes(connection).Select(i => i.Fields), StringComparer.OrdinalIgnoreCase);

                    long rowCount;

                    using (DbCommand countCommand = connection.CreateCommand()) {
                        countCommand.CommandText = $"SELECT count(*) FROM {QualifiedTableName}";
                        var result = countCommand.ExecuteScalar();
                        rowCount = result is not null ? Convert.ToInt64(result, CultureInfo.InvariantCulture) : 0;
                    }

                    var created = 0;

                    foreach (var vectorProperty in Model.VectorProperties) {
                        if (indexedFields.Contains(vectorProperty.StorageName)) {
                            // Already indexed (or Flat, which never reaches here because BuildVectorIndexSql
                            // returns null for it below and the row is simply skipped either way).
                            continue;
                        }

                        var indexSql = DuckDBIndexBuilder.BuildVectorIndexSql(DatasetPath, vectorProperty);

                        if (indexSql is null || rowCount < VectorIndexTrainingRowFloor)
                            continue;

                        using DbCommand createCommand = connection.CreateCommand();
                        createCommand.CommandText = indexSql;
                        createCommand.ExecuteNonQuery();
                        created++;
                    }

                    return created;
                },
                cancellationToken));
    }

    /// <summary>
    /// Runs immediate, on-demand index maintenance for this collection: it first creates any missing vector
    /// index (via <see cref="EnsureVectorIndexesAsync"/>, the catch-up path once the collection has crossed the
    /// 256-row training floor), then append-optimizes every existing vector index so rows written since the index
    /// was last built are folded into it, and finally runs dataset compaction.
    /// </summary>
    /// <remarks>
    /// This is the synchronous counterpart to the background reindex scheduler's periodic tick, for callers that
    /// want to force the indexes current right now (e.g. immediately after a large bulk load, before a
    /// latency-sensitive read). It deliberately does <em>not</em> retrain or vacuum: retrain is time-cadenced
    /// (rebuilding on demand would waste work and ignore the configured interval), and vacuum is
    /// retention-policy-driven (an on-demand vacuum could prune dataset versions that open connections still
    /// cache) — both are owned exclusively by the scheduler.
    /// </remarks>
    /// <param name="cancellationToken">A token to observe while running the operation.</param>
    /// <returns>A task that completes when the maintenance finishes.</returns>
    public async Task OptimizeIndexesAsync(CancellationToken cancellationToken = default) {
        const string OperationName = "OptimizeIndexes";

        // Catch-up creation first, so the append-optimize below has an index to fold rows into on a collection
        // that only just crossed the training floor.
        await EnsureVectorIndexesAsync(cancellationToken).ConfigureAwait(false);

        await VectorStoreErrorHandler
            .RunOperationAsync<DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        // Append-optimize each existing vector index (scalar indexes are left untouched).
                        foreach (var index in ShowIndexes(connection)) {
                            if (DuckDBMaintenanceSql.IsVectorIndexType(index.Type)) {
                                using DbCommand optimizeCommand = connection.CreateCommand();
                                optimizeCommand.CommandText = DuckDBMaintenanceSql.BuildAppendOptimizeSql(index.Name, DatasetPath);
                                optimizeCommand.ExecuteNonQuery();
                            }
                        }

                        // Compaction folds deletion tombstones and small fragments; it is a separate concern from
                        // index freshness, and safe to run on demand.
                        using DbCommand compactCommand = connection.CreateCommand();
                        compactCommand.CommandText = DuckDBMaintenanceSql.BuildCompactionSql(DatasetPath);
                        compactCommand.ExecuteNonQuery();
                    },
                    cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default) {
        const string OperationName = "EnsureCollectionDeleted";

        return VectorStoreErrorHandler.RunOperationAsync<DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync(
                operation: connection => {
                    if (!TableExists(connection))
                        return;

                    using DbCommand dropCommand = connection.CreateCommand();
                    dropCommand.CommandText = $"DROP TABLE {QualifiedTableName}";
                    dropCommand.ExecuteNonQuery();
                },
                cancellationToken));
    }

    /// <summary>
    /// Rejects the collection when any property's storage name collides with a DuckDB reserved word, matched
    /// case-insensitively against <c>duckdb_keywords()</c> (queried once per ensure).
    /// </summary>
    void ValidateNoReservedWordStorageNames(DuckDBAdvancedConnection connection, string operationName) {
        // Storage names reach this layer already lowercased and charset-validated by the model builder, but a
        // reserved word such as "when" or "select" passes that gate while still being illegal as an unquoted
        // SQL identifier. The provider cannot quote its way out: quoting a reserved-word column re-triggers
        // the lance extension's zero-row DELETE bug, so the column could be created but never reliably
        // deleted. Such a model cannot be served correctly and is rejected here — the earliest point a live
        // connection is available to consult the keyword list.
        var reservedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (DbCommand command = connection.CreateCommand()) {
            command.CommandText = "SELECT keyword_name FROM duckdb_keywords() WHERE keyword_category = 'reserved'";
            using var reader  = command.ExecuteReader();
            var       ordinal = reader.GetOrdinal("keyword_name");

            while (reader.Read()) {
                reservedKeywords.Add(reader.GetString(ordinal));
            }
        }

        foreach (var property in Model.Properties) {
            if (reservedKeywords.Contains(property.StorageName)) {
                throw new VectorStoreException(
                    $"The storage name '{property.StorageName}' for property '{property.ModelName}' in collection '{Name}' is a DuckDB reserved word. "
                  + "The DuckLance provider emits column identifiers unquoted (quoting is not viable, because the lance extension's DELETE predicate pushdown "
                  + "silently matches zero rows for a quoted reserved-word column), so a reserved-word storage name cannot be used. Rename the property or set "
                  + "an explicit non-reserved StorageName.") {
                    VectorStoreSystemName = _collectionMetadata.VectorStoreSystemName,
                    VectorStoreName       = _collectionMetadata.VectorStoreName,
                    CollectionName        = _collectionMetadata.CollectionName,
                    OperationName         = operationName
                };
            }
        }
    }

    /// <summary>Determines whether this collection's table currently exists in the attached Lance namespace.</summary>
    bool TableExists(DuckDBAdvancedConnection connection) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM duckdb_tables() WHERE database_name = ? AND table_name = ?";
        command.Parameters.Add(new DuckDBParameter(_connectionManager.StorageAlias));
        command.Parameters.Add(new DuckDBParameter(TableName));

        var result = command.ExecuteScalar();
        var count  = result is not null ? (long)result : 0;

        return count > 0;
    }

    /// <summary>Runs <c>SHOW INDEXES ON '{DatasetPath}'</c> and parses the result.</summary>
    IReadOnlyList<(string Name, string Type, string Fields, long RowsIndexed)> ShowIndexes(DuckDBAdvancedConnection connection) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SHOW INDEXES ON '{EscapeDatasetPath(DatasetPath)}'";

        using var reader = command.ExecuteReader();
        return DuckDBIndexBuilder.ParseShowIndexes(reader);
    }

    /// <summary>
    /// Executes every scalar (<c>BTREE</c>/<c>LABEL_LIST</c>/<c>INVERTED</c>) index statement for the key and
    /// the model's data properties, skipping index names already present in <c>existingIndexNames</c>.
    /// </summary>
    void CreateScalarIndexes(DuckDBAdvancedConnection connection, HashSet<string>? existingIndexNames) {
        // A BTREE index on the key column first: every MERGE upsert joins on `ON t.{key} = s.{key}`, and
        // Get/Delete-by-key filter on the key, so without this index those all degrade to a full dataset scan
        // (measured at ~61s for a 550-row serial upsert). Follows the same `{col}_idx` convention as the data
        // property indexes below, and — like them — is created eagerly against the freshly created (or
        // re-ensured) table, skipping it when an index of that name already exists.
        CreateIndexIfMissing(connection, existingIndexNames, BuildKeyIndexSql());

        foreach (var dataProperty in Model.DataProperties) {
            foreach (var indexSql in DuckDBIndexBuilder.BuildScalarIndexSqls(DatasetPath, dataProperty))
                CreateIndexIfMissing(connection, existingIndexNames, indexSql);
        }
    }

    /// <summary>
    /// Executes the statement unless its declared index name already exists (<c>CREATE INDEX</c> on an existing
    /// name errors); a <see langword="null"/> set means the table was just created and has no indexes yet.
    /// </summary>
    void CreateIndexIfMissing(DuckDBAdvancedConnection connection, HashSet<string>? existingIndexNames, string indexSql) {
        if (existingIndexNames is not null && existingIndexNames.Contains(ExtractIndexName(indexSql)))
            return;

        using DbCommand indexCommand = connection.CreateCommand();
        indexCommand.CommandText = indexSql;
        indexCommand.ExecuteNonQuery();

        // Extracts the index name from a CREATE INDEX statement produced by DuckDBIndexBuilder. The
        // DuckDBIndexBuilder DDL shape is fixed — the literal prefix "CREATE INDEX " followed immediately
        // by the name, then " ON '" — so this slice is a safe, allocation-light parse; no SQL parser needed.
        static string ExtractIndexName(string createIndexSql) {
            const string NamePrefix = "CREATE INDEX ";
            const string NameSuffix = " ON '";

            var nameStart = NamePrefix.Length;
            var nameEnd   = createIndexSql.IndexOf(NameSuffix, nameStart, StringComparison.Ordinal);
            return createIndexSql[nameStart..nameEnd];
        }
    }

    /// <summary>Builds the BTREE <c>CREATE INDEX</c> statement for the key column, matching the <see cref="DuckDBIndexBuilder"/> DDL shape.</summary>
    string BuildKeyIndexSql() {
        var keyColumn = Model.KeyProperty.StorageName;
        return $"CREATE INDEX {keyColumn}_idx ON '{EscapeDatasetPath(DatasetPath)}' ({keyColumn}) USING BTREE";
    }

    /// <summary>Escapes a filesystem path for interpolation into a single-quoted SQL string literal.</summary>
    static string EscapeDatasetPath(string datasetPath) => datasetPath.Replace("'", "''", StringComparison.Ordinal);

    /// <inheritdoc />
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default) {
        const string OperationName = "Get";

        var keyValue       = ConvertKey(key);
        var includeVectors = options?.IncludeVectors ?? false;
        var sql            = DuckDBSqlComposer.BuildSelectByKeySql(QualifiedTableName, Model, includeVectors);

        return VectorStoreErrorHandler.RunOperationAsync<TRecord?, DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync<TRecord?>(
                operation: connection => {
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter(keyValue));

                    using var reader = command.ExecuteReader();
                    return reader.Read() ? _codec.Decode(reader, includeVectors) : null;
                },
                cancellationToken));
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        const string OperationName = "Get";

        List<string> keyValues = [.. keys.Select(ConvertKey)];

        if (keyValues.Count == 0)
            yield break;

        var includeVectors = options?.IncludeVectors ?? false;

        var sql = DuckDBSqlComposer.BuildSelectByKeysSql(
            QualifiedTableName, Model, includeVectors,
            keyValues.Count);

        // DuckDB has no async reader, so materialize the whole result set eagerly inside the single pooled
        // operation, then yield the materialized records. Missing keys are simply absent from the result set.
        var records = await VectorStoreErrorHandler
            .RunOperationAsync<List<TRecord>, DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        using DbCommand command = connection.CreateCommand();
                        command.CommandText = sql;

                        foreach (var keyValue in keyValues)
                            command.Parameters.Add(new DuckDBParameter(keyValue));

                        var       results = new List<TRecord>();
                        using var reader  = command.ExecuteReader();

                        while (reader.Read()) {
                            results.Add(_codec.Decode(reader, includeVectors));
                        }

                        return results;
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        foreach (var record in records)
            yield return record;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        const string OperationName = "Get";

        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        options ??= new();

        var includeVectors = options.IncludeVectors;

        if (includeVectors && Model.EmbeddingGenerationRequired)
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);

        // Translate once, up front: an unsupported filter throws here, before any SQL executes. Both the equality
        // and containment shapes are legal for a plain SELECT — the oversample flag is irrelevant here, since no
        // vector-search function (and therefore no k) is involved; DuckDB simply evaluates the WHERE over the table.
        var filterResult = new DuckDBFilterTranslator().Translate(filter, Model);

        var selectColumns = new List<string>(Model.Properties.Count);

        foreach (var property in Model.Properties) {
            if (property is VectorPropertyModel && !includeVectors)
                continue;

            selectColumns.Add(property.StorageName);
        }

        var skip          = options.Skip;
        var orderByClause = BuildOrderByClause(Model, options);
        var offsetClause  = skip > 0 ? $" OFFSET {skip}" : "";

        var sql =
            $"""
             SELECT {string.Join(", ", selectColumns)} FROM {QualifiedTableName}
             WHERE {filterResult.WhereClause}{orderByClause} LIMIT {top}{offsetClause}
             """;

        var filterParameters = filterResult.Parameters;

        // DuckDB has no async reader, so materialize the whole result set eagerly inside the single pooled
        // operation, then yield the materialized records.
        var records = await VectorStoreErrorHandler
            .RunOperationAsync<List<TRecord>, DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        using DbCommand command = connection.CreateCommand();
                        command.CommandText = sql;

                        foreach (var filterParameter in filterParameters)
                            command.Parameters.Add(new DuckDBParameter(filterParameter ?? DBNull.Value));

                        var       results = new List<TRecord>();
                        using var reader  = command.ExecuteReader();

                        while (reader.Read()) {
                            results.Add(_codec.Decode(reader, includeVectors));
                        }

                        return results;
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        foreach (var record in records)
            yield return record;
    }

    /// <summary>
    /// Runs a caller-composed relational query over this collection's table and decodes each row through
    /// the record codec. The SELECT list is composed HERE — columns in model-property order, per the codec
    /// law — so <paramref name="criteriaSql"/> is only what follows it: WHERE / ORDER BY / LIMIT clauses
    /// referencing storage column names, with one <c>?</c> placeholder per value in
    /// <paramref name="parameters"/> (in placeholder order; null binds as NULL).
    /// </summary>
    /// <remarks>
    /// This is the deliberate escape hatch for consumers that need relational shapes the portable
    /// vector-store surface cannot express — any-of sets (<c>IN</c>), list containment
    /// (<c>array_has_all</c>/<c>array_has_any</c>), server-side ordering over arbitrary columns. The query
    /// still runs on the collection's pooled connections with the standard retry and error handling.
    /// </remarks>
    public Task<IReadOnlyList<TRecord>> QueryAsync(
        string criteriaSql, IReadOnlyList<object?> parameters,
        bool includeVectors = false, CancellationToken cancellationToken = default
    ) {
        const string OperationName = "Query";

        var sql = $"SELECT {DuckDBSqlComposer.BuildColumnList(Model, includeVectors)} FROM {QualifiedTableName} {criteriaSql}";

        return VectorStoreErrorHandler.RunOperationAsync<IReadOnlyList<TRecord>, DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync<IReadOnlyList<TRecord>>(
                operation: connection => {
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = sql;

                    foreach (var value in parameters)
                        command.Parameters.Add(new DuckDBParameter(value ?? DBNull.Value));

                    var       results = new List<TRecord>();
                    using var reader  = command.ExecuteReader();

                    while (reader.Read())
                        results.Add(_codec.Decode(reader, includeVectors));

                    return results;
                },
                cancellationToken));
    }

    /// <summary>
    /// Builds the <c> ORDER BY ...</c> clause (with a single leading space) from the options' sort keys, or
    /// the empty string when none are set; each key resolves to its storage name and carries its own direction.
    /// </summary>
    internal static string BuildOrderByClause(CollectionModel model, FilteredRecordRetrievalOptions<TRecord> options) {
        var orderBy = options.OrderBy?.Invoke(new()).Values;

        if (orderBy is not { Count: > 0 })
            return "";

        var parts = new List<string>(orderBy.Count);

        foreach (var sortInfo in orderBy) {
            var property = model.GetDataOrKeyProperty(sortInfo.PropertySelector);

            // GetDataOrKeyProperty resolves a vector selector to its VectorPropertyModel rather than rejecting it,
            // which would emit `vec ASC` — ordering by a FLOAT[] column, which is meaningless. Reject it here.
            if (property is VectorPropertyModel)
                throw new NotSupportedException("ordering by a vector property is not supported");

            var storageName = property.StorageName;
            parts.Add(sortInfo.Ascending ? $"{storageName} ASC" : $"{storageName} DESC");
        }

        return " ORDER BY " + string.Join(", ", parts);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default) {
        const string OperationName = "Delete";

        var keyValue = ConvertKey(key);
        var sql      = DuckDBSqlComposer.BuildDeleteByKeySql(QualifiedTableName, Model);

        // A missing key deletes zero rows; that is treated as success (no exception).
        return VectorStoreErrorHandler.RunOperationAsync<DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync(
                operation: connection => {
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new DuckDBParameter(keyValue));
                    command.ExecuteNonQuery();
                },
                cancellationToken));
    }

    /// <inheritdoc />
    public override Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default) {
        const string OperationName = "Delete";

        List<string> keyValues = [.. keys.Select(ConvertKey)];

        if (keyValues.Count == 0)
            return Task.CompletedTask;

        var sql = DuckDBSqlComposer.BuildDeleteByKeysSql(QualifiedTableName, Model, keyValues.Count);

        // Missing keys among the set delete zero rows; that is treated as success (no exception).
        return VectorStoreErrorHandler.RunOperationAsync<DbException>(
            _collectionMetadata,
            OperationName,
            operation: () => _connectionManager.ExecuteAsync(
                operation: connection => {
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = sql;

                    foreach (var keyValue in keyValues)
                        command.Parameters.Add(new DuckDBParameter(keyValue));

                    command.ExecuteNonQuery();
                },
                cancellationToken));
    }

    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default) =>
        DoUpsertAsync([record], cancellationToken);

    /// <inheritdoc />
    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default) {
        var recordList = records as IReadOnlyList<TRecord> ?? records.ToList();

        if (recordList.Count == 0)
            return Task.CompletedTask;

        return DoUpsertAsync(recordList, cancellationToken);
    }

    /// <summary>
    /// Validates and upserts the records, vectorizing the whole batch through the codec first —
    /// generation (if any) is batched per vector property, never per record.
    /// </summary>
    async Task DoUpsertAsync(IReadOnlyList<TRecord> records, CancellationToken cancellationToken) {
        const string OperationName = "Upsert";

        foreach (var record in records) {
            ValidateRecordKey(record, nameof(records));
        }

        // De-duplicate keys within the batch, keeping the LAST occurrence's value at the first occurrence's
        // position. This is required for correctness of the multi-row MERGE below: DuckDB evaluates every source
        // row against the pre-merge target, so two source rows sharing a key absent from the target would both
        // take WHEN NOT MATCHED and both be inserted, producing two rows with the same key. Keeping the last
        // occurrence also preserves the observable last-wins semantics of the previous serial per-record loop.
        var uniqueRecords = DeduplicateByKeyLastWins(records);

        // Vectorize the whole batch through the codec — one VectorSlots per record (slots[i] belongs
        // to uniqueRecords[i]), with generation batched per vector property, never per record.
        // Vector-less models skip the call entirely: a hand-written codec for such a record type may
        // legitimately carry no generator, and there is nothing to vectorize anyway.
        var slots = Model.VectorProperties.Count == 0
            ? new VectorSlots[uniqueRecords.Count]
            : await _codec.VectorizeBatchAsync(uniqueRecords, cancellationToken).ConfigureAwait(false);

        // Encode every record to parameter values up front, before any DB work — so a bad record
        // fails the whole call before anything is written.
        var parameterSets = new List<object?[]>(uniqueRecords.Count);

        for (var i = 0; i < uniqueRecords.Count; i++)
            parameterSets.Add(_codec.Encode(uniqueRecords[i], slots[i]));

        var qualifiedTableName = QualifiedTableName;
        var model              = Model;

        await VectorStoreErrorHandler
            .RunOperationAsync<DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        if (parameterSets.Count == 1) {
                            // A single record keeps the existing, validated single-row MERGE shape verbatim.
                            using DbCommand command = connection.CreateCommand();
                            command.CommandText = DuckDBSqlComposer.BuildMergeUpsertSql(qualifiedTableName, model);
                            AddParameters(command, parameterSets[0]);
                            command.ExecuteNonQuery();
                            return;
                        }

                        // Batch path: one multi-row MERGE per chunk — a single atomic Lance commit per chunk instead
                        // of one commit per record, and one planned statement instead of the old O(n²) serial loop.
                        // Chunking bounds the generated SQL length and positional-parameter count for huge batches.
                        for (var start = 0; start < parameterSets.Count; start += MaxUpsertBatchRows) {
                            var chunkRows = Math.Min(MaxUpsertBatchRows, parameterSets.Count - start);

                            using DbCommand command = connection.CreateCommand();
                            command.CommandText = DuckDBSqlComposer.BuildMergeUpsertManySql(qualifiedTableName, model, chunkRows);

                            // Parameters are bound row-major: all of chunk row 0's values (in property order), then
                            // row 1's, ..., matching the placeholder order BuildMergeUpsertManySql emits.
                            for (var i = 0; i < chunkRows; i++)
                                AddParameters(command, parameterSets[start + i]);

                            command.ExecuteNonQuery();
                        }
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        // Binds the values as positional parameters, mapping null to DBNull.Value.
        static void AddParameters(DbCommand command, IReadOnlyList<object?> values) {
            foreach (var value in values)
                command.Parameters.Add(new DuckDBParameter(value ?? DBNull.Value));
        }
    }

    /// <summary>
    /// Collapses duplicate keys, keeping each key's LAST occurrence's value at the position of its FIRST
    /// occurrence; when no key repeats, the input order is preserved.
    /// </summary>
    List<TRecord> DeduplicateByKeyLastWins(IReadOnlyList<TRecord> records) {
        var indexByKey   = new Dictionary<string, int>(records.Count, StringComparer.Ordinal);
        var deduplicated = new List<TRecord>(records.Count);

        foreach (var record in records) {
            // The key was already validated to be a non-null, non-empty string by ValidateRecordKey.
            var key = (string)Model.KeyProperty.GetValueAsObject(record)!;

            if (indexByKey.TryGetValue(key, out var existingIndex))
                deduplicated[existingIndex] = record;
            else {
                indexByKey[key] = deduplicated.Count;
                deduplicated.Add(record);
            }
        }

        return deduplicated;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        const string OperationName = "Search";

        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        options ??= DefaultVectorSearchOptions;

        if (options.IncludeVectors && Model.EmbeddingGenerationRequired)
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);

        var vectorProperty = Model.GetVectorPropertyOrSingle(options);

        // Reject an unsupported distance function up front, before any query is issued.
        DuckDBScoreConverter.ValidateDistanceFunction(vectorProperty.DistanceFunction);

        // ScoreThreshold is applied client-side, AFTER score conversion, on the already-fetched top-k rows: a
        // result within Top that fails the threshold is dropped, so the returned count may be fewer than Top
        // (consistent with the MEVD score-threshold convention). The direction (>= for similarity metrics, <=
        // for distance metrics) is decided by DuckDBScoreConverter.PassesThreshold from the distance function.
        var scoreThreshold = options.ScoreThreshold;

        // Translate the filter once, up front: an unsupported filter throws here — before any SQL is composed or
        // executed — so a filtered search can never silently degrade to an unfiltered or partially filtered one.
        var filter = options.Filter is null
            ? null
            : new DuckDBFilterTranslator().Translate(options.Filter, Model);

        // Resolve the query vector from TInput, mirroring the SqliteVec input switch: direct vector shapes are
        // used as-is; otherwise a configured embedding generator produces the embedding; otherwise the two-way
        // throw distinguishes "no generator configured" from "an incompatible generator configured".
        var vector = searchValue switch {
            ReadOnlyMemory<float> r => r,
            float[] f               => new(f),
            Embedding<float> e      => e.Vector,
            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => ((Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false)).Vector,

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(
                    VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), DuckDBModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(
                    VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

        // PQ-family indexes (and the unindexed brute-force path, which behaves like Dynamic) need a refine pass
        // to keep small-k results accurate; Flat and IVF_FLAT do not.
        var refine = vectorProperty.IndexKind switch {
            null                    => true,
            IndexKind.Dynamic       => true,
            IndexKind.QuantizedFlat => true,
            IndexKind.Hnsw          => true,
            _                       => false
        };

        var skip           = options.Skip;
        var k              = top + skip;
        var includeVectors = options.IncludeVectors;

        var selectColumns = new List<string>(Model.Properties.Count);

        foreach (var property in Model.Properties) {
            if (property is VectorPropertyModel && !includeVectors)
                continue;

            selectColumns.Add(property.StorageName);
        }

        // The unfiltered statement is fully determined here; a filtered statement is composed inside the pooled
        // operation instead, because an oversampling filter must first read the table's row count on the same
        // rented connection before its k can be chosen.
        var unfilteredSql = filter is null
            ? DuckDBSearchSqlComposer.BuildVectorSearchSql(
                QualifiedTableName,
                vectorProperty.StorageName,
                vectorProperty.Dimensions,
                k,
                refine,
                selectColumns,
                top,
                skip)
            : null;

        var vectorParameter  = ToFloatList(vector);
        var distanceFunction = vectorProperty.DistanceFunction;
        var vectorColumn     = vectorProperty.StorageName;
        var dimensions       = vectorProperty.Dimensions;

        // DuckDB has no async reader, so materialize the whole result set eagerly inside the single pooled
        // operation (already ordered by _distance), then yield the materialized results.
        var results = await VectorStoreErrorHandler
            .RunOperationAsync<List<VectorSearchResult<TRecord>>, DbException>(
                _collectionMetadata,
                OperationName,
                operation: () => _connectionManager.ExecuteAsync(
                    operation: connection => {
                        string sql;

                        if (filter is null)
                            sql = unfilteredSql!;
                        else if (filter.RequiresOversample) {
                            // Containment (array_has_any) is NOT pushed into the extension's prefilter IR — DuckDB
                            // evaluates it as a post-filter over whatever neighbors the vector search already returned.
                            // Correctness therefore requires k to cover the WHOLE table (validated: with 2055 rows, a k
                            // below count(*) silently dropped matching rows that a full-table k found). This cost is
                            // inherent to the extension's missing containment pushdown, not a tunable knob.
                            long count;

                            using (DbCommand countCommand = connection.CreateCommand()) {
                                countCommand.CommandText = $"SELECT count(*) FROM {QualifiedTableName}";
                                var countResult = countCommand.ExecuteScalar();
                                count = countResult is not null ? Convert.ToInt64(countResult, CultureInfo.InvariantCulture) : 0;
                            }

                            var oversampleK = (int)Math.Max(count, k);

                            sql = DuckDBSearchSqlComposer.BuildFilteredVectorSearchSql(
                                QualifiedTableName, vectorColumn, dimensions,
                                oversampleK, refine, selectColumns,
                                filter.WhereClause, top, skip);
                        } else {
                            // Equality-only filters push down as a TRUE prefilter (prefilter := true), so the normal
                            // k = top + skip is exact even when matching rows fall outside the global top-k.
                            sql = DuckDBSearchSqlComposer.BuildFilteredVectorSearchSql(
                                QualifiedTableName, vectorColumn, dimensions,
                                k, refine, selectColumns,
                                filter.WhereClause, top, skip);
                        }

                        using DbCommand command = connection.CreateCommand();
                        command.CommandText = sql;

                        // The query vector is the first positional parameter (it lives in the FROM clause); the filter's
                        // parameters follow, in the order their placeholders appear in the WHERE clause.
                        command.Parameters.Add(new DuckDBParameter(vectorParameter));

                        if (filter is not null) {
                            foreach (var filterParameter in filter.Parameters)
                                command.Parameters.Add(new DuckDBParameter(filterParameter ?? DBNull.Value));
                        }

                        var       rows            = new List<VectorSearchResult<TRecord>>();
                        using var reader          = command.ExecuteReader();
                        var       distanceOrdinal = reader.GetOrdinal("_distance");

                        while (reader.Read()) {
                            var record = _codec.Decode(reader, includeVectors);

                            // Lance surfaces _distance as a single-precision FLOAT, which DuckDB.NET materializes as a
                            // System.Single; GetValue + Convert widens it (and tolerates a DOUBLE column) without the
                            // strict-cast InvalidCastException that reader.GetDouble would throw on a Single.
                            var rawDistance = Convert.ToDouble(reader.GetValue(distanceOrdinal), CultureInfo.InvariantCulture);
                            var score       = DuckDBScoreConverter.ConvertScore(distanceFunction, rawDistance);

                            // Post-fetch score-threshold filter: drop this row when it fails the threshold.
                            if (scoreThreshold is not null && !DuckDBScoreConverter.PassesThreshold(distanceFunction, score, scoreThreshold.Value))
                                continue;

                            rows.Add(new(record, score));
                        }

                        return rows;
                    },
                    cancellationToken))
            .ConfigureAwait(false);

        foreach (var result in results)
            yield return result;
    }

    /// <summary>Validates that the record's key resolves to a non-null, non-empty string.</summary>
    void ValidateRecordKey(TRecord record, string paramName) {
        if (Model.KeyProperty.GetValueAsObject(record) is not string key || key.Length == 0)
            throw new ArgumentException("The record key must be a non-null, non-empty string.", paramName);
    }

    /// <summary>
    /// Converts a <typeparamref name="TKey"/> to the non-empty storage key string. <typeparamref name="TKey"/> is
    /// <see cref="string"/> for typed collections and <see cref="object"/> (holding a string) for dynamic ones.
    /// </summary>
    static string ConvertKey(TKey key) {
        var keyValue = (string)(object)key;

        if (keyValue.Length == 0)
            throw new ArgumentException("The key must be a non-empty string.", nameof(key));

        return keyValue;
    }

    /// <summary>Materializes a query vector into the validated <c>List&lt;float&gt;</c> wire shape for <c>CAST(? AS FLOAT[N])</c>.</summary>
    static List<float> ToFloatList(ReadOnlyMemory<float> vector) => [.. vector.Span];
}