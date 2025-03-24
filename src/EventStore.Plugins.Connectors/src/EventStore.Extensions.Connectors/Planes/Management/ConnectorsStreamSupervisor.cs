using EventStore.Core;
using EventStore.Core.Bus;
using Kurrent.Surge;
using Kurrent.Toolkit;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.ConnectorsFeatureConventions.Streams;

using StreamMetadata = EventStore.Core.Data.StreamMetadata;

namespace EventStore.Connectors.Management;

[PublicAPI]
public record ConnectorsStreamSupervisorOptions {
    public SystemStreamOptions Leases      { get; init; }
    public SystemStreamOptions Checkpoints { get; init; }
}

public record SystemStreamOptions(int? MaxCount = null, TimeSpan? MaxAge = null) {
    public StreamMetadata AsStreamMetadata() => new(maxCount: MaxCount, maxAge: MaxAge);
}

/// <summary>
/// Responsible for configuring and deleting the system streams for a connector.
/// </summary>
[PublicAPI]
public class ConnectorsStreamSupervisor(ConnectorsStreamSupervisorOptions options, IPublisher client, ILogger<ConnectorsStreamSupervisor> logger) {
    IPublisher                          Client              { get; } = client;
    ILogger<ConnectorsStreamSupervisor> Logger              { get; } = logger;
    StreamMetadata                      LeasesMetadata      { get; } = options.Leases.AsStreamMetadata();
    StreamMetadata                      CheckpointsMetadata { get; } = options.Checkpoints.AsStreamMetadata();

    public async ValueTask<bool> ConfigureConnectorStreams(string connectorId, CancellationToken ct) {
        await Task.WhenAll(
            TryConfigureStream(GetLeasesStream(connectorId), LeasesMetadata),
            TryConfigureStream(GetCheckpointsStream(connectorId), CheckpointsMetadata)
        );

        return true;

        Task TryConfigureStream(string stream, StreamMetadata metadata) => Client
            .SetStreamMetadata(stream, metadata, cancellationToken: ct)
            .OnError(ex => Logger.LogError(ex, "{ProcessorId} Failed to configure stream {Stream}", connectorId, stream))
            .Then(state => state.Logger.LogDebug("{ProcessorId} Stream {Stream} configured {Metadata}", connectorId, state.Stream, state.Metadata), (Logger, Stream: stream, Metadata: metadata));
    }

    public bool ConfigureConnectorStreams(string connectorId) =>
        ConfigureConnectorStreams(connectorId, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public async ValueTask<bool> DeleteConnectorStream(string connectorId, RecordPosition expectedPosition, CancellationToken ct) {
        await Task.WhenAll(TryDeleteStream(GetLeasesStream(connectorId)),
            TryDeleteStream(GetCheckpointsStream(connectorId)),
            TryTruncateStream(GetManagementStream(connectorId), expectedPosition.StreamRevision)
        );

        return true;

        Task TryDeleteStream(string stream) => Client
            .SoftDeleteStream(stream, ct)
            .OnError(ex => Logger.LogError(ex, "{ProcessorId} Failed to delete stream {Stream}", connectorId, stream))
            .Then(state => state.Logger.LogInformation("{ProcessorId} Stream {Stream} deleted", connectorId, state.Stream), (Logger, Stream: stream));

        Task TryTruncateStream(string stream, long beforeRevision) => Client
            .TruncateStream(stream, beforeRevision, ct)
            .OnError(ex => Logger.LogError(ex, "{ProcessorId} Failed to truncate stream {Stream}", connectorId, stream))
            .Then(state => state.Logger.LogInformation("{ProcessorId} Stream {Stream} truncated", connectorId, state.Stream), (Logger, Stream: stream));
    }
}