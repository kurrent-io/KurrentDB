// ReSharper disable CheckNamespace

namespace EventStore.Streaming.Consumers.Checkpoints;

public static class CheckpointStoreExtensions {
    public static async Task<RecordPosition> ResolveStartPosition(
        this ICheckpointStore checkpointStore,
        SubscriptionInitialPosition initialPosition,
        RecordPosition startPosition,
        CancellationToken cancellationToken = default
    ) {
        if (startPosition != RecordPosition.Unset)
            return startPosition;

        // currently we only support a single consumer per group and no partitions.
        var positions = await checkpointStore.LoadPositions(cancellationToken);

        var subscriptionPosition = positions.Length == 0
            ? initialPosition == SubscriptionInitialPosition.Earliest
                ? RecordPosition.Earliest
                : RecordPosition.Latest
            : positions[^1];

        return subscriptionPosition;
    }
}