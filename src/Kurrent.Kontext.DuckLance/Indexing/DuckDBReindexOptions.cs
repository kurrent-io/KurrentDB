using Microsoft.Extensions.Logging;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Options for index reindexing behavior in the DuckDB vector store.
/// </summary>
public sealed class DuckDBReindexOptions {
    public static readonly DuckDBReindexOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDBReindexOptions"/> class.
    /// </summary>
    public DuckDBReindexOptions() {
        UnindexedRatioThreshold = 0.15;
        UnindexedRowFloor       = 1000;
        TickInterval            = TimeSpan.FromMinutes(5);
        RetrainInterval         = TimeSpan.FromHours(24);
        VacuumRetention         = TimeSpan.FromDays(14);
        VacuumRetainVersions    = 3;
        LoggerFactory           = null;
    }

    internal DuckDBReindexOptions(DuckDBReindexOptions? source) {
        UnindexedRatioThreshold = source?.UnindexedRatioThreshold ?? 0.15;
        UnindexedRowFloor       = source?.UnindexedRowFloor       ?? 1000;
        TickInterval            = source?.TickInterval            ?? TimeSpan.FromMinutes(5);
        RetrainInterval         = source?.RetrainInterval         ?? TimeSpan.FromHours(24);
        VacuumRetention         = source?.VacuumRetention         ?? TimeSpan.FromDays(14);
        VacuumRetainVersions    = source?.VacuumRetainVersions    ?? 3;
        LoggerFactory           = source?.LoggerFactory;
    }

    /// <summary>
    /// Gets or sets the fraction of unindexed rows that triggers an index append-optimize operation.
    /// </summary>
    /// <remarks>
    /// Default is 0.15 (15%).
    /// </remarks>
    public double UnindexedRatioThreshold { get; set; }

    /// <summary>
    /// Gets or sets the minimum absolute number of unindexed rows before the ratio trigger may fire.
    /// </summary>
    /// <remarks>
    /// This avoids over-triggering on small collections. Default is 1000.
    /// </remarks>
    public int UnindexedRowFloor { get; set; }

    /// <summary>
    /// Gets or sets the background scheduler tick period.
    /// </summary>
    /// <remarks>
    /// Default is 5 minutes.
    /// </remarks>
    public TimeSpan TickInterval { get; set; }

    /// <summary>
    /// Gets or sets the time-based full index retrain cadence.
    /// </summary>
    /// <remarks>
    /// Default is 24 hours.
    /// </remarks>
    public TimeSpan RetrainInterval { get; set; }

    /// <summary>
    /// Gets or sets the VACUUM LANCE older_than window.
    /// </summary>
    /// <remarks>
    /// Default is 14 days. WARNING: Must stay comfortably longer than any plausible connection lifetime, as aggressive vacuum breaks cached dataset handles in open connections.
    /// </remarks>
    public TimeSpan VacuumRetention { get; set; }

    /// <summary>
    /// Gets or sets the VACUUM LANCE retain_n_versions count.
    /// </summary>
    /// <remarks>
    /// Default is 3.
    /// </remarks>
    public int VacuumRetainVersions { get; set; }

    /// <summary>
    /// Gets or sets the optional logger factory for scheduler diagnostics.
    /// </summary>
    /// <remarks>
    /// When null, no scheduler diagnostics are logged.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; set; }
}