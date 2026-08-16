// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Modules.Records.Data;

/// <summary>
/// Creates and maintains the records index's physical schema: the lance table, the eager
/// indexes, and the lazily-trained vector index. Resume state is NOT here — the indexer's
/// checkpoint lives in the lance-resident <see cref="KontextCheckpointStore"/> table so it
/// can share the batch transaction.
///
/// Addressing rules (the lance extension's dual addressing):
/// - table DDL uses the qualified name (ldb.main.records — hardcoded, matching the writer)
/// - index DDL uses the RAW dataset path, and inside WITH (...) it is always '=', never ':='
/// </summary>
public sealed class KontextRecordsSchema(KontextConnectionPool connections, KontextSchemaOptions options) {
    const string VectorIndexName = "vec_idx";

    // The eager indexes — neither has a training floor, so both are safe on an empty table:
    // - content_fts (INVERTED): the BM25 side of full-text and hybrid search
    // - log_position_idx (BTREE): range predicates push down as scalars
    static readonly (string Name, string Column, string Method)[] EagerIndexes = [
        ("content_fts", "content", "INVERTED"),
        ("log_position_idx", "log_position", "BTREE"),
    ];

    // The raw dataset path for index DDL, quote-escaped once: paths cannot be bound as
    // parameters in DDL, so it is embedded in the statement text.
    readonly string _datasetPath = Path.Combine(connections.StoragePath, "records.lance").Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// Creates the records table and every eager index. Idempotent — safe to run on every
    /// host start: the table uses IF NOT EXISTS, and indexes are created only when the engine
    /// says they are missing (CREATE INDEX has no validated IF NOT EXISTS shape here).
    /// </summary>
    public async Task CreateAsync(CancellationToken ct = default) {
        if (options.Dimension <= 0)
            throw new InvalidOperationException($"{nameof(KontextSchemaOptions)}.{nameof(options.Dimension)} must be positive and match the embedding model's dimension.");

        // Column order IS the appender's positional append order — the writer adds fields in
        // exactly this sequence. created_at is a BIGINT holding Unix epoch MILLISECONDS (UTC),
        // riding the appender's Add(long) with no session-timezone semantics.
        var createTable =
            $"""
             CREATE TABLE IF NOT EXISTS ldb.main.records (
               log_position BIGINT,
               record_id BLOB,
               stream VARCHAR,
               category VARCHAR,
               schema_name VARCHAR,
               schema_id VARCHAR,
               schema_format VARCHAR,
               content VARCHAR,
               created_at BIGINT,
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
    /// The vector index's ONE lifecycle entry point — call it repeatedly (after write batches)
    /// and it does whatever the index needs right now: creates it once the table can train one,
    /// or folds the unindexed tail in (append-optimize). Returns true when the index exists and
    /// is current; false while the table is below the engine's training floor.
    /// </summary>
    /// <remarks>
    /// While this returns false, vector search is an exact brute-force scan — recall does not
    /// suffer, only latency once the table grows.
    /// </remarks>
    public async Task<bool> EnsureVectorIndexAsync(CancellationToken ct = default) {
        var existing = await ListIndexesAsync(ct).ConfigureAwait(false);

        if (existing.Contains(VectorIndexName)) {
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
            // is the only exact, version-proof check.
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

    /// <summary>The index names that currently exist on the records dataset. Valid only once the table exists.</summary>
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
    // prepared-statement reuse, and bootstrap runs before the writer's connection exists.
    Task ExecuteDdlAsync(string commandText, CancellationToken ct) =>
        connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }, ct);
}
