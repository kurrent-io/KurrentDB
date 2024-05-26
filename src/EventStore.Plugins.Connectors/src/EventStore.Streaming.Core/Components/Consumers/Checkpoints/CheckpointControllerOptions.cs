using EventStore.Streaming.Consumers.Configuration;

namespace EventStore.Streaming.Consumers.Checkpoints;

[PublicAPI]
public record CheckpointControllerOptions {
	public AutoCommitOptions AutoCommit { get; init; } = new();

	public Func<RecordPosition[], CancellationToken, Task> CommitPositions { get; init; } = (_, _) => Task.CompletedTask;

	public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;
}