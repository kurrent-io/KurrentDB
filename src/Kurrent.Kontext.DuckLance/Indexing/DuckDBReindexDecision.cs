using System.Globalization;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// The maintenance action decided for a single vector index on a reindex tick.
/// </summary>
enum DuckDBReindexIndexAction {
    /// <summary>Fold the rows added since the index was last built into it (<c>OPTIMIZE WITH (mode = 'append')</c>).</summary>
    Append,

    /// <summary>Fully rebuild the index from scratch (<c>OPTIMIZE WITH (mode = 'retrain')</c>).</summary>
    Retrain
}

/// <summary>
/// A single decided per-index maintenance operation: the <c>SHOW INDEXES</c> <c>index_name</c> of the vector
/// index to act on, and the action to take on this tick.
/// </summary>
readonly record struct DuckDBReindexIndexOperation(string IndexName, DuckDBReindexIndexAction Action);

/// <summary>
/// The pure, database-free decision for one reindex tick, produced by <see cref="Decide"/> and consumed by
/// <see cref="DuckDBReindexScheduler"/> — the only component that touches the database.
/// </summary>
sealed class DuckDBReindexDecision(
    IReadOnlyList<DuckDBReindexIndexOperation> indexOperations,
    bool retrainRan,
    bool compact,
    bool vacuum
) {
    /// <summary>Gets the per-index append/retrain operations to execute on this tick; empty when none apply.</summary>
    public IReadOnlyList<DuckDBReindexIndexOperation> IndexOperations { get; } = indexOperations;

    /// <summary>
    /// Gets whether at least one retrain operation is present — the scheduler advances its stored last-retrain
    /// clock only when a retrain actually ran (there was at least one index to rebuild).
    /// </summary>
    public bool RetrainRan { get; } = retrainRan;

    /// <summary>Gets a value indicating whether dataset compaction runs on this tick.</summary>
    public bool Compact { get; } = compact;

    /// <summary>Gets a value indicating whether dataset vacuum runs on this tick.</summary>
    public bool Vacuum { get; } = vacuum;

    /// <summary>
    /// Decides the maintenance to perform on one tick, purely from the supplied snapshot — no database access.
    /// </summary>
    public static DuckDBReindexDecision Decide(
        long totalRows,
        IReadOnlyList<(string IndexName, string Fields, long RowsIndexed)> vectorIndexes,
        DuckDBReindexOptions options,
        DateTimeOffset lastRetrain,
        DateTimeOffset now
    ) {
        // Retrain vs. append: when now - lastRetrain >= RetrainInterval a full retrain is due, and EVERY
        // vector index is retrained on this tick (retrain takes precedence over append). Otherwise each
        // vector index is decided independently: it is append-optimized only when its backlog of unindexed
        // rows (totalRows - rows_indexed) is BOTH a large enough fraction of the table AND a large enough
        // absolute count — the floor stops small collections from re-optimizing on every handful of new rows.
        //
        // Compaction and vacuum are cadence-independent, always-run maintenance: they are marked to run on
        // every tick regardless of the per-index decision, even with zero rows or no vector indexes.
        var retrainDue = now - lastRetrain >= options.RetrainInterval;

        var operations = new List<DuckDBReindexIndexOperation>();
        var retrainRan = false;

        // No rows means no vector index can be trained or usefully optimized, so no per-index work is emitted;
        // an empty index list naturally yields no operations either.
        if (totalRows > 0) {
            foreach (var (indexName, _, rowsIndexed) in vectorIndexes) {
                if (retrainDue) {
                    operations.Add(new(indexName, DuckDBReindexIndexAction.Retrain));
                    retrainRan = true;
                    continue;
                }

                var unindexed = totalRows - rowsIndexed;

                // Ratio uses strict '>' (exactly at the threshold does NOT trigger); floor uses '>=' (exactly at
                // the floor DOES trigger). Both must hold for an append.
                if (unindexed                     >= options.UnindexedRowFloor
                 && (double)unindexed / totalRows > options.UnindexedRatioThreshold)
                    operations.Add(new(indexName, DuckDBReindexIndexAction.Append));
            }
        }

        return new(
            operations, retrainRan, true,
            true);
    }
}

/// <summary>
/// Composes the SQL statements DuckLance issues for on-demand and scheduled index maintenance against a Lance
/// dataset's raw filesystem path, and classifies <c>SHOW INDEXES</c> rows as vector vs. scalar indexes.
/// </summary>
static class DuckDBMaintenanceSql {
    // All statements target the raw dataset path (never the attached table name), mirroring
    // DuckDBIndexBuilder. Inside a WITH clause, options always use '=' — never Lance's alternate ':='
    // syntax, which this extension version rejects.

    /// <summary>Determines whether a <c>SHOW INDEXES</c> <c>index_type</c> value names a Lance vector index family.</summary>
    public static bool IsVectorIndexType(string indexType) {
        // SHOW INDEXES reports the type in a squashed PascalCase form (IvfPq, IvfFlat, IvfHnswPq for vector
        // indexes; BTree, LabelList, Inverted for scalar ones). Every vector index family this provider
        // creates (IVF_PQ, IVF_FLAT, IVF_HNSW_PQ — see DuckDBIndexKindMapper.GetLanceIndexType) starts with
        // "IVF" once underscores are stripped and case is ignored, and no scalar family does — so a
        // normalized IVF prefix cleanly selects vector indexes without depending on the exact
        // casing/separators the extension happens to report.
        if (string.IsNullOrEmpty(indexType))
            return false;

        return indexType
            .Replace("_", "", StringComparison.Ordinal)
            .StartsWith("IVF", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds the append-optimize statement that folds unindexed rows into an existing vector index.</summary>
    public static string BuildAppendOptimizeSql(string indexName, string datasetPath) =>
        $"ALTER INDEX {indexName} ON '{EscapePath(datasetPath)}' OPTIMIZE WITH (mode = 'append')";

    /// <summary>Builds the retrain-optimize statement that fully rebuilds an existing vector index.</summary>
    public static string BuildRetrainOptimizeSql(string indexName, string datasetPath) =>
        $"ALTER INDEX {indexName} ON '{EscapePath(datasetPath)}' OPTIMIZE WITH (mode = 'retrain')";

    /// <summary>
    /// Builds the dataset compaction statement (folds deletion tombstones and small fragments) — a genuinely
    /// separate operation from index freshness.
    /// </summary>
    public static string BuildCompactionSql(string datasetPath) =>
        $"OPTIMIZE '{EscapePath(datasetPath)}' WITH (materialize_deletions = true, materialize_deletions_threshold = 0.1)";

    /// <summary>Builds the dataset vacuum statement (prunes old dataset versions).</summary>
    public static string BuildVacuumSql(string datasetPath, long olderThanSeconds, int retainVersions) =>
        // Retention must stay conservative: an aggressive vacuum prunes dataset versions that open
        // connections still hold cached handles to, breaking those handles (the stale-dataset-handle
        // failure the connection manager then has to recover from).
        string.Create(
            CultureInfo.InvariantCulture,
            $"VACUUM LANCE '{EscapePath(datasetPath)}' WITH (older_than_seconds = {olderThanSeconds}, retain_n_versions = {retainVersions})");

    /// <summary>Escapes a filesystem path for interpolation into a single-quoted SQL string literal.</summary>
    public static string EscapePath(string datasetPath) => datasetPath.Replace("'", "''", StringComparison.Ordinal);
}