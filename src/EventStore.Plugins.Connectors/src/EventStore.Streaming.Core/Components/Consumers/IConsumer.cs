namespace EventStore.Streaming.Consumers;

public interface IConsumerMetadata {
	/// <summary>
	/// The name of the consumer.
	/// </summary>
	public string ConsumerName { get; }

	/// <summary>
	/// The name of the subscription. 
	/// </summary>
	public string SubscriptionName { get; }

	/// <summary>
	/// The streams that the consumer is subscribed to.
	/// </summary>
	public string[] Streams { get; }

	/// <summary>
	/// The filter used to filter out the records sent by the server for processing.
	/// </summary>
	public ConsumeFilter Filter { get; }
	
	// /// <summary>
	// /// The positions matching the records sent by the server for processing.
	// /// </summary>
	// public IReadOnlyList<RecordPosition> CommittedPositions { get; }
	//
	// /// <summary>
	// /// The positions matching the records processed by the consumer to be committed.
	// /// </summary>
	// public IReadOnlyList<RecordPosition> TrackedPositions { get; }
}

public interface IConsumer : IConsumerMetadata, IAsyncDisposable {
	// /// <summary>
	// /// Creates the subscription or finds it and reads the position.
	// /// </summary>
	// /// <param name="stoppingToken"></param>
	// /// <returns></returns>
	// public Task Subscribe(CancellationToken stoppingToken);
	
	/// <summary>
	/// Commits positions (if auto commit is enabled) and alerts the group coordinator
	/// that the consumer is exiting the group then releases all resources used by this consumer.
	/// </summary>
	public Task Unsubscribe();
	
	/// <summary>
	///	Consumes the records from the stream.
	/// </summary>
	/// <param name="stoppingToken">
	///	A cancellation token that can be used to cancel the operation.
	/// </param>
	/// <returns>
	///	A sequence of <see cref="EventStoreRecord" />.
	/// </returns>
	public IAsyncEnumerable<EventStoreRecord> Records(CancellationToken stoppingToken = default);

	/// <summary>
	///	Tracks the consumed record position in memory.
	/// </summary>
	public Task Track(EventStoreRecord record);
	
	/// <summary>
	///	Commits all tracked positions to EventStore effectively acknowledging all the records as processed.
	/// </summary>
	public Task Commit();
	
	//public Task Commit(EventStoreRecord record);
	
	//public Task Commit(params RecordPosition[] positions);

	/// <summary>
	/// Returns the latest positions for all streams.
	/// </summary>
	public Task<IReadOnlyList<RecordPosition>> GetLatestPositions(CancellationToken cancellationToken = default);
}