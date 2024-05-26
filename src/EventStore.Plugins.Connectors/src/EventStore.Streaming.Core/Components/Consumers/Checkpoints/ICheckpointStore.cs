namespace EventStore.Streaming.Consumers.Checkpoints;

public interface ICheckpointStore {
	string GroupId            { get; }
	string ConsumerId         { get; }
	string CheckpointStreamId { get; }
	bool   Initialized        { get; }

	Task Initialize(CancellationToken cancellationToken = default);

	Task<RecordPosition[]> GetLatestPositions(CancellationToken cancellationToken = default);

	Task<RecordPosition[]> CommitPositions(RecordPosition[] positions, CancellationToken cancellationToken = default);
}