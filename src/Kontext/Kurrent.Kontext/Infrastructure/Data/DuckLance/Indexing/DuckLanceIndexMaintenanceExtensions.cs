// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Kurrent.Quack;
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

namespace Kurrent.Kontext.Infrastructure.Data.LanceDB;

/// <summary>
/// Index DDL over a lance dataset: create, drop, optimize and inspect. Every statement takes a
/// qualified table (<c>ldb.main.records</c>) so the surface is reusable outside Kontext.
/// </summary>
public static class DuckLanceIndexMaintenanceExtensions {
    extension(DuckDBAdvancedConnection connection) {
        /// <summary>Creates a vector index, named <c>{column}_ivx</c>.</summary>
        public void CreateVectorIndex(string table, string column, ILanceVectorIndexOptions options) {
            options.EnsureValid();

            var indexName  = LanceIndexNames.Vector(column);
            var indexType  = options.IndexType.Token;
            var parameters = Render(options.Parameters());

            var sql =
                $"""
                 CREATE INDEX {indexName} ON {table} ({column})
                     USING {indexType}
                     WITH ({parameters})
                 """;

            connection.ExecuteAdHocNonQuery(sql);
        }

        /// <summary>Creates a vector index of <typeparamref name="TOptions"/>, configured inline.</summary>
        public void CreateVectorIndex<TOptions>(string table, string column, Action<TOptions> configure)
            where TOptions : ILanceVectorIndexOptions, new() {
            var options = new TOptions();
            configure(options);

            connection.CreateVectorIndex(table, column, options);
        }

        /// <summary>Creates an inverted (FTS) index, named <c>{column}_fts</c>.</summary>
        public void CreateInvertedIndex(string table, string column, LanceInvertedIndexOptions options, bool replace = false) {
            var indexName  = LanceIndexNames.Inverted(column);
            var parameters = Render(options.Parameters());

            var sql =
                $"""
                 CREATE INDEX {indexName} ON {table} ({column})
                     USING INVERTED
                     WITH ({parameters})
                 """;

            connection.ExecuteAdHocNonQuery(sql);
        }

        /// <summary>Creates an inverted (FTS) index, configured inline.</summary>
        public void CreateInvertedIndex(string table, string column, Action<LanceInvertedIndexOptions> configure) {
            var options = new LanceInvertedIndexOptions();
            configure(options);

            connection.CreateInvertedIndex(table, column, options);
        }

        /// <summary>Creates a scalar index, named <c>{column}_idx</c>, configured inline.</summary>
        public void CreateScalarIndex(string table, string column, Action<LanceScalarIndexOptions> configure) {
            var options = new LanceScalarIndexOptions();
            configure(options);

            connection.CreateScalarIndex(table, column, options);
        }

        /// <summary>Creates a scalar index, named <c>{column}_idx</c>.</summary>
        public void CreateScalarIndex(string table, string column, LanceScalarIndexOptions options) {
            var indexName  = LanceIndexNames.Scalar(column);
            var indexType  = options.Type.Token;
            var parameters = Render(options.Parameters());

            var sql =
                $"""
                 CREATE INDEX {indexName} ON {table} ({column})
                     USING {indexType}
                     WITH ({parameters})
                 """;

            connection.ExecuteAdHocNonQuery(sql);
        }

        /// <summary>
        /// The vector index's lifecycle entry point — call it repeatedly and it does whatever the
        /// index needs now: creates it, folds the unindexed tail in, or rebuilds it when the
        /// existing index is of another type. Returns false while the table is below the engine's
        /// training floor, where search falls back to an exact scan.
        /// </summary>
        public bool EnsureVectorIndex(string table, string column, ILanceVectorIndexOptions options, bool replaceMismatchedType = true) {
            var name     = LanceIndexNames.Vector(column);
            var existing = connection.GetTableInfo(table)?.FindIndex(name);

            if (existing is not null) {
                if (!replaceMismatchedType || existing.IndexType.Equals(options.IndexType.Token, StringComparison.OrdinalIgnoreCase)) {
                    connection.OptimizeIndex(table, name);
                    return true;
                }

                connection.DropIndex(table, name);
            }

            try {
                connection.CreateVectorIndex(table, column, options);
            } catch (Exception ex) when (IsBelowTrainingFloor(ex)) {
                return false;
            }

            return true;
            
            // The engine's refusal to train on too few rows, in BOTH validated wordings:
            // - empty table:     "Creating empty vector indices with train=False is not yet implemented"
            // - below the floor: "Not enough rows to train PQ. Requires 256 rows but only 5 available"
            static bool IsBelowTrainingFloor(Exception ex) {
                var text = ex.ToString();
                return text.Contains("Not enough rows to train", StringComparison.Ordinal)
                    || text.Contains("Creating empty vector indices", StringComparison.Ordinal);
            }
        }

        /// <summary>Ensures a vector index of <typeparamref name="TOptions"/>, configured inline.</summary>
        public bool EnsureVectorIndex<TOptions>(string table, string column, Action<TOptions> configure, bool replaceMismatchedType = true)
            where TOptions : ILanceVectorIndexOptions, new() {
            var options = new TOptions();
            configure(options);

            return connection.EnsureVectorIndex(table, column, options, replaceMismatchedType);
        }

        /// <summary>
        /// Folds the unindexed tail into the inverted (FTS) index. Over unfolded rows
        /// <c>lance_fts</c> returns the FIRST k rows by scan arrival instead of the top k by BM25,
        /// so this is correctness, not latency. Returns false when the table carries no such
        /// index, because ALTER INDEX on a missing index is a silent no-op.
        /// </summary>
        public bool EnsureInvertedIndex(string table, string column) {
            var name = LanceIndexNames.Inverted(column);

            if (connection.GetTableInfo(table)?.FindIndex(name) is null)
                return false;

            connection.OptimizeIndex(table, name);
            return true;
        }

        /// <summary>Drops an index. Throws when it does not exist.</summary>
        public void DropIndex(string table, string name) {
            var sql = $"DROP INDEX {name} ON {table}";
            connection.ExecuteAdHocNonQuery(sql);
        }

        /// <summary>
        /// Folds newly written rows into an index, merges its deltas, or retrains it from scratch.
        /// A missing index is a SILENT no-op, so check <see cref="FindIndex"/> first.
        /// </summary>
        public void OptimizeIndex(string table, string name, LanceOptimizeIndexOptions? options = null) {
            options ??= new();
            options.EnsureValid();

            var parameters = Render(options.Parameters());

            var sql =
                $"""
                 ALTER INDEX {name} ON {table}
                     OPTIMIZE WITH ({parameters})
                 """;

            connection.ExecuteAdHocNonQuery(sql);
        }

        /// <summary>Refreshes an index, configured inline.</summary>
        public void OptimizeIndex(string table, string name, Action<LanceOptimizeIndexOptions> configure) {
            var options = new LanceOptimizeIndexOptions();
            configure(options);

            connection.OptimizeIndex(table, name, options);
        }

        /// <summary>Folds deletion tombstones and small fragments back into compact form.</summary>
        public void CompactTable(string table, double deletionThreshold = 0.1) {
            var threshold = deletionThreshold.ToString(CultureInfo.InvariantCulture);

            var sql =
                $"""
                 OPTIMIZE {table}
                     WITH (materialize_deletions = true, materialize_deletions_threshold = {threshold})
                 """;

            connection.ExecuteAdHocNonQuery(sql);
        }

        /// <summary>
        /// The table's state in one snapshot — row count and every index — read on one connection
        /// so the numbers describe the same dataset version. Null when the table does not exist.
        /// </summary>
        public LanceTableInfo? GetTableInfo(string table) {
            var (catalog, name) = LanceTableName.Split(table);

            var existsSql =
                $"""
                 SELECT count(*)
                 FROM duckdb_tables()
                 WHERE database_name = '{catalog}'
                   AND table_name = '{name}'
                 """;

            using var exists = connection.CreateCommand();
            exists.CommandText = existsSql;

            if ((long)exists.ExecuteScalar()! == 0)
                return null;

            var rowsSql = $"SELECT count(*) FROM {table}";

            using var rows = connection.CreateCommand();
            rows.CommandText = rowsSql;

            var rowCount = (long)rows.ExecuteScalar()!;

            var indexesSql = $"SHOW INDEXES ON {table}";

            var indexes = new List<LanceIndexInfo>();

            // SHOW INDEXES: index_name | index_type | fields | rows_indexed | details.
            // rows_indexed is NULL while an index exists but has folded nothing yet.
            using var result = connection.ExecuteAdHocQuery(indexesSql);

            while (result.TryFetch(out var chunk)) {
                while (chunk.TryRead(out var row))
                    indexes.Add(new(
                        Name: row.ReadString(),
                        IndexType: row.ReadString(),
                        Column: row.ReadString(),
                        RowsIndexed: (long?)row.TryReadUInt64(),
                        Details: row.TryReadString()));

                chunk.Dispose();
            }

            return new(table, rowCount, indexes);
        }
    }

    static string Render(IEnumerable<(string Name, string Value)> parameters) =>
        string.Join(", ", parameters.Select(p => $"{p.Name} = {p.Value}"));
}

/// <summary>IVF partition sizing: lance sizes IVF_PQ and IVF_RQ at one partition per 4096 rows.</summary>
public static class LancePartitions {
    public const int DefaultRowsPerPartition = 4096;

    public static int For(long rows, int rowsPerPartition = DefaultRowsPerPartition) =>
        (int)Math.Max(1, rows / rowsPerPartition);
}

/// <summary>Splits a qualified lance table name into its catalog and table parts.</summary>
static class LanceTableName {
    public static (string Catalog, string Table) Split(string qualified) {
        // ldb.main.records -> catalog 'ldb', table 'records'. duckdb_tables() indexes by those two.
        var parts = qualified.Split('.');

        return parts.Length switch {
            3 => (parts[0], parts[2]),
            2 => (parts[0], parts[1]),
            _ => throw new ArgumentException($"'{qualified}' is not a qualified table name.", nameof(qualified)),
        };
    }
}

/// <summary>Derived index names: the engine registers none, so every lookup composes one.</summary>
public static class LanceIndexNames {
    public static string Vector(string column) => $"{column}_ivx";

    public static string Inverted(string column) => $"{column}_fts";

    public static string Scalar(string column) => $"{column}_idx";
}

/// <summary>A table's state at one dataset version.</summary>
/// <param name="Name">The qualified table name.</param>
/// <param name="RowCount">Rows at this version.</param>
/// <param name="Indexes">Every index on the table.</param>
public sealed record LanceTableInfo(string Name, long RowCount, IReadOnlyList<LanceIndexInfo> Indexes) {
    /// <summary>The named index, or null when the table carries none by that name.</summary>
    public LanceIndexInfo? FindIndex(string name) =>
        Indexes.FirstOrDefault(index => index.Name.Equals(name, StringComparison.Ordinal));

    /// <summary>Rows the named index has not folded yet — the whole table when it does not exist.</summary>
    public long UnindexedRows(string name) => RowCount - (FindIndex(name)?.RowsIndexed ?? 0);
}

/// <summary>One index as SHOW INDEXES reports it.</summary>
/// <param name="Name">The index name.</param>
/// <param name="IndexType">The engine's family, e.g. <c>IVF_PQ</c>, <c>Inverted</c>, <c>BTree</c>.</param>
/// <param name="Column">The column it covers.</param>
/// <param name="RowsIndexed">Rows folded in; null while it has folded nothing yet.</param>
/// <param name="Details">The engine's per-index JSON.</param>
public sealed record LanceIndexInfo(string Name, string IndexType, string Column, long? RowsIndexed, string? Details);

public enum LanceOptimizeMode {
    /// <summary>Folds rows written since the last build into the existing index, as a new delta.</summary>
    Append,

    /// <summary>Merges the newest deltas into one, so search consults fewer of them.</summary>
    Merge,

    /// <summary>Rebuilds from the table's current rows, re-training the quantizer.</summary>
    Retrain,
}

public enum LanceScalarIndexType {
    /// <summary>Ordered scalar lookups and range predicates.</summary>
    BTree,

    /// <summary>Membership over list columns.</summary>
    LabelList,

    /// <summary>Equality over high-cardinality scalars.</summary>
    Bitmap,
}

/// <summary>The vector index families the vendored build accepts.</summary>
public enum LanceVectorIndexType {
    /// <summary>Exact vectors in IVF partitions.</summary>
    IvfFlat,

    /// <summary>IVF + product quantization.</summary>
    IvfPq,

    /// <summary>IVF + residual quantization.</summary>
    IvfRq,

    /// <summary>IVF + scalar quantization.</summary>
    IvfSq,

    /// <summary>HNSW over exact vectors.</summary>
    IvfHnswFlat,

    /// <summary>HNSW over product-quantized vectors.</summary>
    IvfHnswPq,

    /// <summary>HNSW over scalar-quantized vectors.</summary>
    IvfHnswSq,
}

/// <summary>The USING token each index type is named by. Spelled out, never derived from the member name.</summary>
public static class LanceIndexTypeNames {
    extension(LanceVectorIndexType type) {
        public string Token => type switch {
            LanceVectorIndexType.IvfFlat     => "IVF_FLAT",
            LanceVectorIndexType.IvfPq       => "IVF_PQ",
            LanceVectorIndexType.IvfRq       => "IVF_RQ",
            LanceVectorIndexType.IvfSq       => "IVF_SQ",
            LanceVectorIndexType.IvfHnswFlat => "IVF_HNSW_FLAT",
            LanceVectorIndexType.IvfHnswPq   => "IVF_HNSW_PQ",
            LanceVectorIndexType.IvfHnswSq   => "IVF_HNSW_SQ",
        };
    }

    extension(LanceScalarIndexType type) {
        public string Token => type switch {
            LanceScalarIndexType.BTree     => "BTREE",
            LanceScalarIndexType.LabelList => "LABEL_LIST",
            LanceScalarIndexType.Bitmap    => "BITMAP",
        };
    }
}

/// <summary>The distance metric an index is built for. Must match how vectors are compared at search time.</summary>
public enum LanceMetricType {
    L2,
    Cosine,
    Dot,
    Hamming,
}

/// <summary>A vector index's build parameters. One implementation per lance index type.</summary>
public interface ILanceVectorIndexOptions {
    /// <summary>The lance index family.</summary>
    LanceVectorIndexType IndexType { get; }

    /// <summary>The WITH clause parameters, without <c>replace</c>.</summary>
    IEnumerable<(string Name, string Value)> Parameters();

    /// <summary>Throws when the values cannot produce a valid index.</summary>
    void EnsureValid();
}
