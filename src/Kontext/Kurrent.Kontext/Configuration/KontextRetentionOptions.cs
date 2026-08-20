namespace Kurrent.Kontext.Configuration;

/// <summary>
/// The dataset version-pruning policy the bootstrap asserts on every table at startup — the
/// engine then prunes old versions itself, on its own commit cadence (AUTO_CLEANUP). A plain
/// mutable settings class so it binds from configuration.
/// </summary>
public sealed class KontextRetentionOptions {
    /// <summary>Commits between the engine's cleanup passes. Default is 100.</summary>
    public int Interval { get; set; } = 100;

    /// <summary>
    /// The version age window — only versions older than this are pruned. Default is 14 days.
    /// Must stay comfortably longer than any plausible connection lifetime: pruning a version
    /// under a live connection's cached view is the stale-dataset-handle failure.
    /// </summary>
    public TimeSpan OlderThan { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The minimum number of newest versions always kept. Default is 3.</summary>
    public int RetainVersions { get; set; } = 3;

    /// <summary>Throws when the policy cannot run: a non-positive interval, window, or retain count.</summary>
    public void EnsureValid() {
        if (Interval < 1)
            throw new InvalidOperationException($"{nameof(Interval)} must be at least 1 commit.");

        if (OlderThan <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(OlderThan)} must be a positive window.");

        if (RetainVersions < 1)
            throw new InvalidOperationException($"{nameof(RetainVersions)} must keep at least 1 version.");
    }
}