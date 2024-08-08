using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.ConnectorsFeatureConventions.Streams;

namespace EventStore.Connectors.Management.Reactors;

[PublicAPI]
public record ConnectorsStreamSupervisorOptions {
    public SystemStreamOptions Leases      { get; init; } = new(MaxCount: 1);
    public SystemStreamOptions Checkpoints { get; init; } = new(MaxCount: 3);
    public SystemStreamOptions Lifetime    { get; init; } = new(MaxCount: 5);
}

public record SystemStreamOptions(int? MaxCount = null, TimeSpan? MaxAge = null) {
    public StreamMetadata AsStreamMetadata() => new(maxCount: MaxCount, maxAge: MaxAge);
}

/// <summary>
/// Responsible for configuring and deleting the system streams for a connector.
/// </summary>
public class ConnectorsStreamSupervisor : ProcessingModule {
    public ConnectorsStreamSupervisor(IPublisher client, ConnectorsStreamSupervisorOptions options) {
        var leasesMetadata      = options.Leases.AsStreamMetadata();
        var checkpointsMetadata = options.Checkpoints.AsStreamMetadata();
        var lifetimeMetadata    = options.Lifetime.AsStreamMetadata();

        Process<ConnectorCreated>(async (evt, ctx) => {
            await TryConfigureStream(GetLeasesStream(evt.ConnectorId), leasesMetadata);
            await TryConfigureStream(GetCheckpointsStream(evt.ConnectorId), checkpointsMetadata);
            await TryConfigureStream(GetLifecycleStream(evt.ConnectorId), lifetimeMetadata);

            return;

            Task TryConfigureStream(string stream, StreamMetadata metadata) => client
                .SetStreamMetadata(stream, metadata, cancellationToken: ctx.CancellationToken)
                .OnError(ex => ctx.Logger.LogError(ex, "Failed to configure stream {Stream}", stream))
                .Then(state =>
                        state.Logger.LogDebug("Stream {Stream} configured {Metadata}", state.Stream, state.Metadata),
                    (ctx.Logger, Stream: stream, Metadata: metadata));
        });

        Process<ConnectorDeleted>(async (evt, ctx) => {
            await TryDeleteStream(GetLeasesStream(evt.ConnectorId));
            await TryDeleteStream(GetLifecycleStream(evt.ConnectorId));
            await TryDeleteStream(GetCheckpointsStream(evt.ConnectorId));
            await TryTruncateStream(GetManagementStream(evt.ConnectorId), ctx.Record.Position.StreamRevision);

            return;

            Task TryDeleteStream(string stream) => client
                .SoftDeleteStream(stream, ctx.CancellationToken)
                .OnError(ex => ctx.Logger.LogError(ex, "Failed to delete stream {Stream}", stream))
                .Then(state => state.Logger.LogInformation("Stream {Stream} deleted", state.Stream),
                    (ctx.Logger, Stream: stream));

            Task TryTruncateStream(string stream, long beforeRevision) => client
                .TruncateStream(stream, beforeRevision, ctx.CancellationToken)
                .OnError(ex => ctx.Logger.LogError(ex, "Failed to truncate stream {Stream}", stream))
                .Then(state => state.Logger.LogInformation("Stream {Stream} truncated", state.Stream),
                    (ctx.Logger, Stream: stream));
        });
    }
}