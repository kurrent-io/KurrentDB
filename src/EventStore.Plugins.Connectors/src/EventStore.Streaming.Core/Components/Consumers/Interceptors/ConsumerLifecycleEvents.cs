// ReSharper disable CheckNamespace

using EventStore.Streaming.Interceptors;

namespace EventStore.Streaming.Consumers.LifecycleEvents;

public abstract record ConsumerLifecycleEvent(IConsumerMetadata Consumer) : InterceptorEvent();

public record RecordReceived(IConsumerMetadata Consumer, EventStoreRecord Record) : ConsumerLifecycleEvent(Consumer);

public record RecordTracked(IConsumerMetadata Consumer, EventStoreRecord Record) : ConsumerLifecycleEvent(Consumer);

public record PartitionsAssigned(IConsumerMetadata Consumer, IReadOnlyList<RecordPosition> Positions) : ConsumerLifecycleEvent(Consumer);

public record PartitionsRevoked(IConsumerMetadata Consumer, IReadOnlyList<RecordPosition> Positions) : ConsumerLifecycleEvent(Consumer);

public record PartitionEndReached(IConsumerMetadata Consumer, RecordPosition Position) : ConsumerLifecycleEvent(Consumer);

public record PositionsCommitted(IConsumerMetadata Consumer, IReadOnlyList<RecordPosition> Positions) : ConsumerLifecycleEvent(Consumer);

public record PositionsCommitError(IConsumerMetadata Consumer, IReadOnlyList<RecordPosition> Positions, Exception Error) : ConsumerLifecycleEvent(Consumer);

public record ConsumerUnsubscribed(IConsumerMetadata Consumer, Exception? Error = null) : ConsumerLifecycleEvent(Consumer);

public record ConsumerStopped(IConsumerMetadata Consumer, Exception? Error = null) : ConsumerLifecycleEvent(Consumer);

public record ConnectionErrorDetected(IConsumerMetadata Consumer, Exception Error) : ConsumerLifecycleEvent(Consumer);

