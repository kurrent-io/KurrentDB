// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Vector-index maintenance and hygiene over any lance dataset: the lazily-trained vector
/// index, the time-based retrain, compaction, and the state probes the maintenance scheduler
/// decides on. Table and eager-index DDL is bootstrap, not maintenance — it lives in
/// <see cref="KontextSchemaTask"/> on the migration stream, and version pruning is the
/// dataset's own AUTO_CLEANUP policy, set there too.
///
/// Addressing rules (the lance extension's dual addressing): index DDL uses the RAW dataset
/// path, and inside WITH (...) it is always '=', never ':='.
/// </summary>
public static class KontextIndexMaintenance {
    extension(KontextDataSource dataSource) {
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
        public bool EnsureVectorIndex(string table, string column, VectorIndexOptions? options = null) {
            if (dataSource.GetIndexInfo(table).Name is { } vector) {
                var alterIndex = $"ALTER INDEX {vector} ON ldb.main.{table} OPTIMIZE WITH (mode = 'append')";
                ExecuteCommand(dataSource, alterIndex);
                return true;
            }
            
            options ??= new();
            options.EnsureValid();

            var createIndex =
                $"""
                 CREATE INDEX {VectorIndexName(column)} 
                     ON ldb.main.{table} ({column}) USING IVF_HNSW_PQ
                 WITH (
                   metric_type = '{options.MetricType}',
                   num_partitions = {options.NumPartitions},
                   num_sub_vectors = {options.NumSubVectors},
                   num_bits = {options.NumBits},
                   hnsw_m = {options.HnswM},
                   hnsw_ef_construction = {options.HnswEfConstruction})
                 """;

            try {
                ExecuteCommand(dataSource, createIndex);
            } catch (Exception ex) when (IsBelowTrainingFloor(ex)) {
                // The training floor (~256 rows) is the ENGINE's internal rule — asking by trying
                // is the only exact, version-proof check. A client-side count against a copied
                // constant would drift the day the engine changes its rule.
                return false;
            }

            return true;

           static bool IsBelowTrainingFloor(Exception ex) {
               // The engine's refusal to train on too few rows, in BOTH validated wordings:
               // - empty table:     "Creating empty vector indices with train=False is not yet implemented"
               // - below the floor: "Not enough rows to train PQ. Requires 256 rows but only 5 available"

               var text = ex.ToString();
               return text.Contains("Not enough rows to train", StringComparison.Ordinal)
                   || text.Contains("Creating empty vector indices", StringComparison.Ordinal);
           }
        }

        /// <summary>
        /// Fully rebuilds the vector index from the table's current rows (<c>OPTIMIZE WITH (mode = 'retrain')</c>),
        /// re-training the quantizer that append folds slowly drift away from. A missing or
        /// misnamed index makes this a SILENT no-op — the engine reports success and does
        /// nothing — so only call it when <see cref="GetIndexInfo"/> shows the index exists.
        /// </summary>
        public void RetrainVectorIndex(string table, string column) =>
            ExecuteCommand(dataSource, $"ALTER INDEX {VectorIndexName(column)} ON ldb.main.{table} OPTIMIZE WITH (mode = 'retrain')");

        /// <summary>
        /// Folds deletion tombstones and small fragments back into compact form — dataset hygiene,
        /// a genuinely separate operation from index freshness.
        /// </summary>
        public void Compact(string table) =>
            ExecuteCommand(dataSource, $"OPTIMIZE ldb.main.{table} WITH (materialize_deletions = true, materialize_deletions_threshold = 0.1)");

        /// <summary>
        /// Asserts the dataset's version-pruning policy: the engine then prunes old versions
        /// itself, every <see cref="KontextRetentionOptions.Interval"/> commits. SET overwrites
        /// any previous policy — the initial policy ships with the schema (v1); this is the
        /// runtime knob for changing it on an existing store.
        /// </summary>
        public void SetAutoCleanup(string table, KontextRetentionOptions retention) {
            retention.EnsureValid();

            // The window renders in whole seconds — the one duration grammar probed on the engine.
            ExecuteCommand(
                dataSource,
                $"ALTER TABLE ldb.main.{table} SET AUTO_CLEANUP WITH (interval = {retention.Interval}, older_than = '{(long)retention.OlderThan.TotalSeconds}s', retain_versions = {retention.RetainVersions})");
        }

        /// <summary>
        /// Determines whether the table exists yet — the maintenance scheduler's quiet-skip
        /// probe for ticks that fire before the migration stream (<see cref="KontextSchemaTask"/>) has run.
        /// </summary>
        public bool Exists(string table) =>
            dataSource.Execute(
                connection => {
                    // duckdb_tables() lists tables across every attached catalog; database_name is the
                    // ATTACH alias. Ad-hoc takes no parameters — both predicates embed.
                    using var result = connection.ExecuteAdHocQuery(
                        $"SELECT count(*) FROM duckdb_tables() WHERE database_name = 'ldb' AND table_name = '{table}'");

                    if (!result.TryFetch(out var chunk))
                        return false;

                    // count(*) always yields exactly one row.
                    chunk.TryRead(out var row);

                    var exists = row.ReadInt64() > 0;
                    chunk.Dispose();
                    return exists;
                });

        /// <summary>
        /// One consistent snapshot of the dataset's vector-index state, read on ONE connection so
        /// every number describes the same lance dataset version. Valid only once the table exists.
        /// </summary>
        public VectorIndexInfo GetIndexInfo(string table) =>
            dataSource.Execute(
                connection => {
                    // Two ad-hoc queries, back-to-back on the SAME connection: the ad-hoc surface
                    // has no multi-result command, and SHOW INDEXES is parser-extension grammar —
                    // strictly top-level, it cannot nest in FROM/CTAS/CTE, so filtering happens here.
                    var totalRows = CountRows(connection, table);

                    // SHOW INDEXES output, one row per index, columns read in declaration order:
                    //
                    //   index_name     index_type   fields     rows_indexed  details
                    //   varchar        varchar      varchar    uint64        varchar
                    //   content_fts    Inverted     content    300           {"ascii_folding":true,...}
                    //   embedding_ivx  IVF_HNSW_PQ  embedding  300           {"metric_type":"L2",...}
                    //
                    // rows_indexed is NULL while an index exists but has folded nothing yet.
                    using var result = connection.ExecuteAdHocQuery($"SHOW INDEXES ON ldb.main.{table}");

                    while (result.TryFetch(out var chunk)) {
                        while (chunk.TryRead(out var row)) {
                            var name = row.ReadString();

                            if (!IsVectorIndex(name))
                                continue;

                            var indexType   = row.ReadString();
                            var column      = row.ReadString();
                            var rowsIndexed = row.TryReadUInt64();
                            var details     = row.TryReadString();

                            chunk.Dispose();

                            return new VectorIndexInfo(totalRows, name, indexType, column, (long)(rowsIndexed ?? 0), details);
                        }

                        chunk.Dispose();
                    }

                    return new VectorIndexInfo(totalRows, null, null, null, null, null);

                    static bool IsVectorIndex(string name) => name.EndsWith("_ivx", StringComparison.Ordinal);

                    // ExecuteAdHocNonQuery's long is duckdb rows_changed (INSERT/UPDATE/DELETE only),
                    // so a scalar SELECT must come through a query result.
                    static long CountRows(DuckDBAdvancedConnection connection, string table) {
                        using var count = connection.ExecuteAdHocQuery($"SELECT count(*) FROM ldb.main.{table}");

                        count.TryFetch(out var chunk);
                        chunk.TryRead(out var row); // count(*) always yields exactly one row

                        var rows = row.ReadInt64();
                        chunk.Dispose();
                        return rows;
                    }
                });
    }

    // Naming convention: {column}_ivx IS the vector index — the name is derived, never
    // registered, and classification goes by the suffix, never by sniffing index_type.
    static string VectorIndexName(string column) => $"{column}_ivx";

    // The char overload — NOT the utf8 one: that parameter is documented null-terminated, and
    // Encoding.GetBytes produces no terminator.
    static void ExecuteCommand(KontextDataSource dataSource, string sql) =>
        dataSource.Execute(connection => connection.ExecuteAdHocNonQuery(sql));
}

/// <summary>
/// One consistent snapshot of a dataset's vector-index state, read on one connection so every
/// number describes the same lance dataset version.
/// </summary>
/// <param name="TotalRows">The table's total row count at the snapshot's dataset version.</param>
/// <param name="Name">The <c>*_ivx</c> vector index name; null while no vector index exists.</param>
/// <param name="IndexType">The engine's index family as SHOW INDEXES reports it (e.g. <c>IVF_HNSW_PQ</c>); null while no vector index exists.</param>
/// <param name="Column">The column the index covers; null while no vector index exists.</param>
/// <param name="RowsIndexed">The rows folded into the index: null while no index exists, 0 while the index exists but has folded nothing yet.</param>
/// <param name="Details">The engine's per-index JSON (metric, HNSW and PQ parameters, runtime hints); null while no vector index exists.</param>
public sealed record VectorIndexInfo(
    long    TotalRows,
    string? Name,
    string? IndexType,
    string? Column,
    long?   RowsIndexed,
    string? Details
) {
    /// <summary>The rows the index has not folded yet — the whole table while no index exists.</summary>
    public long UnindexedRows => TotalRows - (RowsIndexed ?? 0);
}

/// <summary>
/// The IVF_HNSW_PQ creation knobs. A mutable settings class by design — config binding does
/// not cope with records. <see cref="EnsureValid"/> owns the invariants; creation calls it
/// before any DDL runs.
/// </summary>
public sealed class VectorIndexOptions {
    /// <summary>The distance metric the index is built for: <c>l2</c>, <c>cosine</c> or <c>dot</c>. Must match how the stored vectors are compared at search time.</summary>
    public string MetricType { get; set; } = "l2";

    /// <summary>The IVF partition count. One partition keeps ANN divergence confined to near-ties;
    /// refine_factor at SEARCH time re-ranks with exact distances, which is what makes the PQ
    /// quantization safe.</summary>
    public int NumPartitions { get; set; } = 1;

    /// <summary>The PQ sub-vector count. Must evenly divide the embedding dimension — and the
    /// dimension itself always does (1-dimension sub-vectors, the validated configuration).</summary>
    public int NumSubVectors { get; set; } = KontextSchemaTask.Dimension;

    /// <summary>Bits per PQ code — 4 or 8, the quantizer's supported widths. More bits, better recall, larger index.</summary>
    public int NumBits { get; set; } = 8;

    /// <summary>The HNSW graph degree — max connections per node. Higher degrees improve recall and cost memory.</summary>
    public int HnswM { get; set; } = 16;

    /// <summary>The HNSW build-time candidate beam. Larger beams build better graphs, slower.</summary>
    public int HnswEfConstruction { get; set; } = 100;

    /// <summary>Throws when the knobs cannot produce a valid index: an unknown metric, a
    /// non-positive count, a PQ width outside 4/8, sub-vectors that do not divide the embedding
    /// dimension, or a build beam narrower than the graph degree.</summary>
    public void EnsureValid() {
        if (MetricType is not ("l2" or "cosine" or "dot"))
            throw new InvalidOperationException($"{nameof(MetricType)} '{MetricType}' is not a lance metric; use 'l2', 'cosine' or 'dot'.");

        if (NumPartitions < 1)
            throw new InvalidOperationException($"{nameof(NumPartitions)} must be at least 1.");

        if (NumSubVectors < 1 || KontextSchemaTask.Dimension % NumSubVectors != 0)
            throw new InvalidOperationException($"{nameof(NumSubVectors)} must be positive and evenly divide the embedding dimension ({KontextSchemaTask.Dimension}).");

        if (NumBits is not (4 or 8))
            throw new InvalidOperationException($"{nameof(NumBits)} must be 4 or 8 — the PQ quantizer's supported widths.");

        if (HnswM < 1)
            throw new InvalidOperationException($"{nameof(HnswM)} must be at least 1.");

        if (HnswEfConstruction < HnswM)
            throw new InvalidOperationException($"{nameof(HnswEfConstruction)} must be at least {nameof(HnswM)} — the build beam selects the graph's neighbors and cannot be narrower than the degree.");
    }
}
