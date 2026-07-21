// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Creates and maintains the memories read model's physical schema: the lance table, the eager
/// indexes, and the lazily-trained vector index.
///
/// A separate component on purpose:
/// - <see cref="KontextDataStoreV2"/> is read-only by design — it assumes the schema exists
/// - the projector owns data writes and should not carry DDL
/// - the host gets ONE place to bootstrap storage before either of them touches the table
///
/// Addressing rules (the lance extension's dual addressing):
/// - table DDL uses the qualified name (ldb.main.memories — hardcoded, matching the store)
/// - index DDL uses the RAW dataset path, and inside WITH (...) it is always '=', never ':='
/// </summary>
public sealed class KontextSchema(KontextConnectionPool connections, KontextSchemaOptions options) {
    const string VectorIndexName = "vec_idx";

    // The eager indexes — none has a training floor, so all are safe on an empty table:
    // - content_fts (INVERTED): the BM25 side of full-text and hybrid search — without it
    //   lance_fts and the keyword leg of lance_hybrid_search have nothing to rank with
    // - memory_id_idx / superseded_by_idx (BTREE): the equality columns — point gets and both
    //   directions of the lineage walk are '=' lookups, and equality pushes down into the scan
    // - tags_idx (LABEL_LIST): the tag containment column. DORMANT TODAY: the extension does
    //   not push containment down (the validated oversample rule exists because of that), so
    //   this index waits for the engine to learn the translation — created now so existing
    //   data is already covered the day that lands.
    static readonly (string Name, string Column, string Method)[] EagerIndexes = [
        ("content_fts", "content", "INVERTED"),
        ("memory_id_idx", "memory_id", "BTREE"),
        ("superseded_by_idx", "superseded_by", "BTREE"),
        ("tags_idx", "tags", "LABEL_LIST"),
    ];

    // The raw dataset path for index DDL, quote-escaped once: paths cannot be bound as
    // parameters in DDL, so it is embedded in the statement text.
    readonly string _datasetPath = Path.Combine(connections.StoragePath, "memories.lance").Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// Creates the memories table and every eager index. Idempotent — safe to run on every
    /// host start: the table uses IF NOT EXISTS, and indexes are created only when the engine
    /// says they are missing (CREATE INDEX has no validated IF NOT EXISTS shape here).
    /// </summary>
    public async Task CreateAsync(CancellationToken ct = default) {
        // The dimension is part of the column TYPE (FLOAT[N]) — the one thing that can never
        // be a parameter — and it must match the retrieval pipeline's embedding model.
        if (options.Dimension <= 0)
            throw new InvalidOperationException($"{nameof(KontextSchemaOptions)}.{nameof(options.Dimension)} must be the embedding model's dimension; it has no safe default.");

        var createTable =
            $"""
             CREATE TABLE IF NOT EXISTS ldb.main.memories (
               memory_id VARCHAR,
               memory_type INTEGER,
               content VARCHAR,
               importance INTEGER,
               sentiment INTEGER,
               urgency INTEGER,
               tags VARCHAR[],
               evidence BLOB,
               cited_memory_ids VARCHAR[],
               supersedes VARCHAR[],
               validity_start TIMESTAMPTZ,
               validity_end TIMESTAMPTZ,
               retained_at TIMESTAMPTZ,
               last_accessed_at TIMESTAMPTZ,
               is_retracted BOOLEAN,
               retracted_at TIMESTAMPTZ,
               is_superseded BOOLEAN,
               superseded_at TIMESTAMPTZ,
               superseded_by VARCHAR,
               embedding FLOAT[{options.Dimension}])
             """;

        await ExecuteDdlAsync(createTable, ct).ConfigureAwait(false);

        var existing = await ListIndexesAsync(ct).ConfigureAwait(false);

        foreach (var (name, column, method) in EagerIndexes) {
            if (existing.Contains(name))
                continue;

            var createIndex = $"CREATE INDEX {name} ON '{_datasetPath}' ({column}) USING {method}";

            await ExecuteDdlAsync(createIndex, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The vector index's ONE lifecycle entry point — call it repeatedly (after write batches,
    /// or on a timer) and it does whatever the index needs right now:
    /// - no index and the table can train one => creates it
    /// - no index and the table is below the engine's training floor => returns false, come back later
    /// - index exists => folds the unindexed tail in (append-optimize), keeping it current
    /// Returns true when the index exists and is current; false while below the floor.
    /// </summary>
    /// <remarks>
    /// While this returns false, vector search is an exact brute-force scan — recall does not
    /// suffer, only latency once the table grows. New rows written after the index was built
    /// are never missing either: they live in an unindexed tail that queries scan brute-force
    /// and merge — the optimize here bounds that tail's latency cost, not correctness.
    /// </remarks>
    public async Task<bool> EnsureVectorIndexAsync(CancellationToken ct = default) {
        if (options.Dimension <= 0)
            throw new InvalidOperationException($"{nameof(KontextSchemaOptions)}.{nameof(options.Dimension)} must be the embedding model's dimension; it has no safe default.");

        var existing = await ListIndexesAsync(ct).ConfigureAwait(false);

        if (existing.Contains(VectorIndexName)) {
            // Maintenance addresses the raw dataset path too, and inside WITH it is '=' —
            // mode = 'append' folds only the rows written since the last build/optimize.
            var appendOptimize = $"ALTER INDEX {VectorIndexName} ON '{_datasetPath}' OPTIMIZE WITH (mode = 'append')";

            await ExecuteDdlAsync(appendOptimize, ct).ConfigureAwait(false);

            return true;
        }

        // num_sub_vectors must evenly divide the dimension, and the dimension itself always
        // does (1-dimension sub-vectors — the validated configuration). num_partitions = 1
        // keeps ANN divergence confined to near-ties; refine_factor at SEARCH time re-ranks
        // with exact distances, which is what makes the PQ quantization safe.
        var createIndex =
            $"""
             CREATE INDEX {VectorIndexName} ON '{_datasetPath}' (embedding) USING IVF_HNSW_PQ
             WITH (
               metric_type = 'l2',
               num_partitions = 1,
               num_sub_vectors = {options.Dimension},
               num_bits = 8,
               hnsw_m = 16,
               hnsw_ef_construction = 100)
             """;

        try {
            await ExecuteDdlAsync(createIndex, ct).ConfigureAwait(false);
        } catch (Exception ex) when (IsBelowTrainingFloor(ex)) {
            // The training floor (~256 rows) is the ENGINE's internal rule — asking by trying
            // is the only exact, version-proof check. A client-side count against a copied
            // constant would drift the day the engine changes its rule.
            return false;
        }

        return true;
    }

    // The engine's refusal to train on too few rows, in BOTH validated wordings:
    // - empty table:     "Creating empty vector indices with train=False is not yet implemented"
    // - below the floor: "Not enough rows to train PQ. Requires 256 rows but only 5 available"
    // Anything else is a real failure and must propagate.
    static bool IsBelowTrainingFloor(Exception ex) {
        var text = ex.ToString();

        return text.Contains("Not enough rows to train", StringComparison.Ordinal)
            || text.Contains("Creating empty vector indices", StringComparison.Ordinal);
    }

    /// <summary>
    /// Fully rebuilds the vector index from the table's current rows (<c>OPTIMIZE WITH (mode = 'retrain')</c>),
    /// re-training the quantizer that append folds slowly drift away from. Only call this when the
    /// index exists — retraining a missing index is an engine error.
    /// </summary>
    public Task RetrainVectorIndexAsync(CancellationToken ct = default) {
        var commandText = $"ALTER INDEX {VectorIndexName} ON '{_datasetPath}' OPTIMIZE WITH (mode = 'retrain')";

        return ExecuteDdlAsync(commandText, ct);
    }

    /// <summary>
    /// Folds deletion tombstones and small fragments back into compact form — dataset hygiene,
    /// a genuinely separate operation from index freshness.
    /// </summary>
    public Task CompactAsync(CancellationToken ct = default) {
        var commandText = $"OPTIMIZE '{_datasetPath}' WITH (materialize_deletions = true, materialize_deletions_threshold = 0.1)";

        return ExecuteDdlAsync(commandText, ct);
    }

    /// <summary>Prunes dataset versions older than <paramref name="olderThan"/>, always keeping the newest <paramref name="retainVersions"/>.</summary>
    public Task VacuumAsync(TimeSpan olderThan, int retainVersions, CancellationToken ct = default) {
        // Retention must stay conservative: an aggressive vacuum prunes dataset versions that open
        // connections still hold cached views of, breaking those handles (the stale-dataset-handle
        // failure the pool then has to recycle). The two numbers are embedded, not bound — DDL takes
        // no parameters, the same rule that embeds the dataset path.
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"VACUUM LANCE '{_datasetPath}' WITH (older_than_seconds = {(long)olderThan.TotalSeconds}, retain_n_versions = {retainVersions})");

        return ExecuteDdlAsync(commandText, ct);
    }

    /// <summary>
    /// Determines whether the memories table exists yet — the maintenance scheduler's quiet-skip
    /// probe for ticks that fire before <see cref="CreateAsync"/> has run.
    /// </summary>
    public Task<bool> ExistsAsync(CancellationToken ct = default) {
        // duckdb_tables() lists tables across every attached catalog; database_name is the ATTACH alias.
        const string commandText =
            """
            SELECT count(*)
            FROM duckdb_tables()
            WHERE database_name = $database_name
              AND table_name = $table_name
            """;

        return connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = commandText;
                command.Parameters.Add(new("database_name", "ldb"));
                command.Parameters.Add(new("table_name", "memories"));
                return (long)command.ExecuteScalar()! > 0;
            }, ct);
    }

    /// <summary>
    /// Reads the maintenance snapshot in one round trip: the table's total rows, and the vector
    /// index's <c>rows_indexed</c> (<see langword="null"/> while the index does not exist — the
    /// unindexed tail is <c>TotalRows - VectorIndexRows</c>). Valid only once the table exists.
    /// </summary>
    public Task<(long TotalRows, long? VectorIndexRows)> GetMaintenanceStateAsync(CancellationToken ct = default) {
        // Two statements, ONE command: the count and the index probe describe the same instant on
        // the same connection. SHOW INDEXES' validated shape is exactly the columns
        // index_name, index_type, fields, rows_indexed, details.
        var commandText =
            $"""
             SELECT count(*) FROM ldb.main.memories;
             SHOW INDEXES ON '{_datasetPath}'
             """;

        return connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = commandText;

                using var reader = command.ExecuteReader();

                reader.Read();
                var totalRows = reader.GetInt64(0);

                long? vectorIndexRows = null;

                reader.NextResult();

                var nameOrdinal = reader.GetOrdinal("index_name");
                var rowsOrdinal = reader.GetOrdinal("rows_indexed");

                while (reader.Read()) {
                    if (reader.GetString(nameOrdinal) != VectorIndexName)
                        continue;

                    // A NULL rows_indexed means the index exists but has folded nothing yet.
                    vectorIndexRows = reader.IsDBNull(rowsOrdinal)
                        ? 0L
                        : Convert.ToInt64(reader.GetValue(rowsOrdinal), CultureInfo.InvariantCulture);

                    break;
                }

                return (totalRows, vectorIndexRows);
            }, ct);
    }

    /// <summary>The index names that currently exist on the memories dataset. Valid only once the table exists.</summary>
    public Task<List<string>> ListIndexesAsync(CancellationToken ct = default) {
        var commandText = $"SHOW INDEXES ON '{_datasetPath}'";

        return connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = commandText;

                var       names  = new List<string>();
                using var reader = command.ExecuteReader();

                while (reader.Read())
                    names.Add(reader.GetString(0));

                return names;
            }, ct);
    }

    // DDL runs through the rented read surface on purpose: it needs no transaction and no
    // prepared-statement reuse, bootstrap runs before any writer exists, and a maintenance
    // call that races the projector surfaces a lance commit conflict to ITS caller — the
    // scheduler owns the retry, exactly like the writer owns its own.
    Task ExecuteDdlAsync(string commandText, CancellationToken ct) =>
        connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }, ct);
}

/// <summary>
/// The schema's physical knobs. A mutable settings class by design — config binding does not
/// cope with records.
/// </summary>
public sealed class KontextSchemaOptions {
    /// <summary>
    /// The embedding dimension — the N in the FLOAT[N] column type. No default on purpose:
    /// it must match the retrieval pipeline's embedding model exactly, and a silently wrong
    /// dimension would poison every stored vector.
    /// </summary>
    public int Dimension { get; set; }
}
