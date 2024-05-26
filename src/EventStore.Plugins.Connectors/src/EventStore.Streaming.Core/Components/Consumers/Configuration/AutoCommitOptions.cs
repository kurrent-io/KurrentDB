namespace EventStore.Streaming.Consumers.Configuration;

[PublicAPI]
public record AutoCommitOptions {
	public static readonly TimeSpan DefaultInterval         = TimeSpan.FromSeconds(5);
	public static readonly int      DefaultRecordsThreshold = 4000;
	
	/// <summary>
	/// Automatically and periodically commit the latest tracked positions to checkpoint store.
	/// <para />
	/// [ Default: true | Importance: high ]
	/// </summary>
	public bool AutoCommitEnabled { get; init; } = true;

	/// <summary>
	/// The frequency that the latest tracked positions are committed (written) to checkpoint storage.
	/// <para />
	/// [ Default: 5 seconds | Importance: high ]
	/// </summary>
	public TimeSpan Interval { get; init; } = DefaultInterval;

	/// <summary>
	/// The number of records that a consumer can track before attempting to write a checkpoint.
	/// If the consumer is set to checkpoint every 4,000 records, but it only reads from the `foo` stream, the consumer only checkpoints every 4,000 `foo` records.
	/// A record is also considered handled (skipped actually) if it actually passed through the processor's filter.
	/// <para />
	/// [ Default: 4000 | Importance: high ]
	/// </summary>
	public int RecordsThreshold { get; init; } = DefaultRecordsThreshold;
}