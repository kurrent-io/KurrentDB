namespace EventStore.Streaming.Consumers.Checkpoints;

public interface ICheckpointController : IAsyncDisposable {
	public IEnumerable<RecordPosition> Positions { get; }

	/// <summary>
	/// Runs initialization routines and possibly activates the timer to
	/// start committing record positions at the given configured interval.
	/// </summary>
	/// <param name="stoppingToken">
	/// A <see cref="CancellationToken"/> that can be used to stop the checkpoint manager.
	/// </param>
	public Task Activate(CancellationToken stoppingToken = default);

	/// <summary>
	/// Keeps track of a processed record position in-memory
	/// </summary>
	/// <param name="record">The record to track as processed/acknowledged in-memory.</param>
	public Task Track(EventStoreRecord record);

	/// <summary>
	/// Attempts to commit the latest tracked record positions to the server.
	/// </summary>
	/// <param name="cancellationToken">
	///     A <see cref="CancellationToken"/> that can be used to cancel the commit operation.
	/// </param>
	public Task<RecordPosition[]> Commit(CancellationToken cancellationToken = default);
	
	/// <summary>
	///  Stops the timer and commits the latest tracked record positions to the server.
	/// </summary>
	public ValueTask Deactivate();
}