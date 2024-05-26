using EventStore.Streaming.Persistence.State;

namespace EventStore.Streaming.Consumers.Checkpoints;

public class InMemoryCheckpointStore(string groupId, string consumerId) : ICheckpointStore {
    InMemoryStateStore Cache { get; } = new InMemoryStateStore();

    public string GroupId            { get; } = groupId;
    public string ConsumerId         { get; } = consumerId;
    public string CheckpointStreamId { get; } = $"$consumer-positions-{groupId}";
    public bool   Initialized        { get; } = true;

    public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public async Task<RecordPosition[]> GetLatestPositions(CancellationToken cancellationToken = default) {
		var positions = await Cache.Get<RecordPosition[]>(CheckpointStreamId, cancellationToken);
		return positions ?? [];
	}

	public async Task<RecordPosition[]> CommitPositions(RecordPosition[] positions, CancellationToken cancellationToken = default) {
		await Cache.Set(CheckpointStreamId, positions, cancellationToken);
		return positions;
	}
}