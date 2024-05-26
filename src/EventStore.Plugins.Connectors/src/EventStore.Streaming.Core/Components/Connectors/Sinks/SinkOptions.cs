// ReSharper disable CheckNamespace

namespace EventStore.IO.Connectors.Configuration;

public record SinkOptions {
    public string ConnectorName { get; init; } = "";
    public string SinkTypeName  { get; init; } = "";

    public SinkAutoCommitOptions   AutoCommit   { get; init; } = new();
    public SinkAuditOptions        Audit        { get; init; } = new();
    public SinkSubscriptionOptions Subscription { get; init; } = new();
    public SinkLoggingOptions      Logging      { get; init; } = new();
    
    public virtual void EnsureValid() {
        if (string.IsNullOrWhiteSpace(SinkTypeName))
            throw new("Sink type name is not specified.");

        if (string.IsNullOrWhiteSpace(ConnectorName))
            throw new("Connector name is not specified.");

        if (string.IsNullOrWhiteSpace(Subscription.SubscriptionName))
            throw new("Subscription name is not specified.");

        if (Subscription.ConsumeFilter.Scope == SinkConsumeFilterScope.Unspecified)
            throw new("Consume filter scope is not specified.");
    }
}

public record SinkSubscriptionOptions {
    public string                          SubscriptionName { get; init; } = ""; // group id?
    public ClusterNodeAffinity             NodeAffinity     { get; init; } = ClusterNodeAffinity.Leader;
    public SinkSubscriptionInitialPosition InitialPosition  { get; init; } = SinkSubscriptionInitialPosition.Latest;
    public SinkConsumeFilter               ConsumeFilter    { get; init; } = new();

    // public ulong?         StartPosition { get; init; }
    // public RecordPosition StartPosition { get; init; } = RecordPosition.Unset; // "stream:partition@position"

    // public int                          ConsumerMaxPollIntervalMs { get; init; } = 3000; // consumer.max.poll.interval.ms
    // public int                          ConsumerMaxPollRecords    { get; init; } = 100;  // consumer.max.poll.records
    // public BehaviourForNullValueRecords NullValueBehavior         { get; init; } = BehaviourForNullValueRecords.Ignore;
    // public BehaviorOnErrors             ErrorBehavior             { get; init; } = BehaviorOnErrors.Fail;
}

public record SinkLoggingOptions {
    public bool Enabled { get; init; } = false;
}

public enum ClusterNodeAffinity {
    Any,
    Leader,
    Follower,
    ReadOnlyReplica,
}

public record SinkAuditOptions {
    public bool   Enabled  { get; init; } = false;
    public string StreamId { get; init; } = "";
}

public record SinkAutoCommitOptions {
    public bool Enabled          { get; init; } = true; // why would we disable it? think about it
    public int  Interval         { get; init; } = 5000;
    public int  RecordsThreshold { get; init; } = 1000;
}

public enum SinkSubscriptionInitialPosition {
    /// <summary>
    /// Consumption will start at the last message.
    /// </summary>
    Latest = 0,

    /// <summary>
    /// Consumption will start at the first message.
    /// </summary>
    Earliest = 1
}

public enum SinkConsumeFilterScope {
    Unspecified = 0,
    Stream      = 1,
    Record      = 2
}

public record SinkConsumeFilter {
    public SinkConsumeFilterScope Scope      { get; init; } = SinkConsumeFilterScope.Unspecified;
    public string                 Expression { get; init; } = "";
}

// 'null.value.behavior' 'behavior.on.null.values'
public enum BehaviourForNullValueRecords {
    Ignore,
    Fail
}

public enum BehaviorOnErrors {
    Ignore,
    Fail
}