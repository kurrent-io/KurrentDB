using Microsoft.Extensions.AI;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Options when creating a DuckDBVectorStore.
/// </summary>
public sealed class DuckDBVectorStoreOptions {
    internal static readonly DuckDBVectorStoreOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDBVectorStoreOptions"/> class.
    /// </summary>
    public DuckDBVectorStoreOptions() {
        DatabasePath   = "";
        ReindexOptions = new();
    }

    internal DuckDBVectorStoreOptions(DuckDBVectorStoreOptions? source) {
        DatabasePath       = source?.DatabasePath ?? "";
        EmbeddingGenerator = source?.EmbeddingGenerator;
        ExtensionPath      = source?.ExtensionPath;
        MemoryLimitMib     = source?.MemoryLimitMib;
        ReindexOptions     = source?.ReindexOptions != null ? new DuckDBReindexOptions(source.ReindexOptions) : new();
        Codecs             = source?.Codecs != null ? new RecordCodecRegistry(source.Codecs) : new();
    }

    /// <summary>
    /// Gets or sets the full path of the DuckDB database file, name included (e.g. <c>duck.db</c>,
    /// <c>/data/kontext/donald.duck</c>).
    /// </summary>
    /// <remarks>
    /// Everything else follows by convention: the file's directory is attached as the Lance namespace and holds
    /// one <c>{collection}.lance</c> dataset per collection. The file name's stem (up to the first <c>.</c>) must
    /// not equal the attach alias (<c>ldb</c>) — DuckDB names the database's own catalog after the stem, and a
    /// colliding name would make the Lance attach silently no-op.
    /// </remarks>
    public string DatabasePath { get; set; }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vector embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>
    /// Gets or sets the optional path to a pre-downloaded lance .duckdb_extension file for air-gapped environments.
    /// </summary>
    /// <remarks>
    /// When null, the extension is INSTALLed from the network (cached by DuckDB).
    /// </remarks>
    public string? ExtensionPath { get; set; }

    /// <summary>
    /// Gets or sets an optional cap, in mebibytes (MiB), on the DuckDB engine's memory budget.
    /// </summary>
    /// <remarks>
    /// Maps to DuckDB's <c>memory_limit</c> setting, applied to the backing engine via the connection string
    /// (<c>memory_limit={value}MiB</c>). When <see langword="null"/> (the default), DuckDB's own default memory
    /// budget is used and no <c>memory_limit</c> is set.
    /// </remarks>
    public int? MemoryLimitMib { get; set; }

    /// <summary>
    /// Gets or sets the shared reindex options for every collection's scheduler.
    /// </summary>
    public DuckDBReindexOptions ReindexOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the per-record-type codec registrations. A registered codec wins; every other
    /// record type falls back to the model-driven <see cref="DuckDBModelCodec{TRecord}"/>.
    /// </summary>
    public RecordCodecRegistry Codecs { get; set; } = new();
}