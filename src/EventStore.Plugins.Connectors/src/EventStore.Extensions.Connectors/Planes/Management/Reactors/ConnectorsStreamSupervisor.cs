using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming.Processors;
using EventStore.Toolkit;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.ConnectorsFeatureConventions.Streams;

namespace EventStore.Connectors.Management.Reactors;

[PublicAPI]
public record ConnectorsStreamSupervisorOptions {
    public SystemStreamOptions Leases      { get; init; }
    public SystemStreamOptions Checkpoints { get; init; }
    public SystemStreamOptions Lifetime    { get; init; }
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
                .OnError(ex => ctx.Logger.LogError(ex, "{ProcessorId} Failed to configure stream {Stream}", ctx.Processor.ProcessorId, stream))
                .Then(state =>
                    state.Logger.LogDebug("{ProcessorId} Stream {Stream} configured {Metadata}", ctx.Processor.ProcessorId, state.Stream, state.Metadata),
                    (ctx.Logger, Stream: stream, Metadata: metadata)
                );
        });

        Process<ConnectorDeleted>(async (evt, ctx) => {
            await TryDeleteStream(GetLeasesStream(evt.ConnectorId));
            await TryDeleteStream(GetLifecycleStream(evt.ConnectorId));
            await TryDeleteStream(GetCheckpointsStream(evt.ConnectorId));
            await TryTruncateStream(GetManagementStream(evt.ConnectorId), ctx.Record.Position.StreamRevision);

            return;

            Task TryDeleteStream(string stream) => client
                .SoftDeleteStream(stream, ctx.CancellationToken)
                .OnError(ex => ctx.Logger.LogError(ex, "{ProcessorId} Failed to delete stream {Stream}", ctx.Processor.ProcessorId, stream))
                .Then(state => state.Logger.LogInformation("{ProcessorId} Stream {Stream} deleted", ctx.Processor.ProcessorId, state.Stream),
                    (ctx.Logger, Stream: stream));

            Task TryTruncateStream(string stream, long beforeRevision) => client
                .TruncateStream(stream, beforeRevision, ctx.CancellationToken)
                .OnError(ex => ctx.Logger.LogError(ex, "{ProcessorId} Failed to truncate stream {Stream}", ctx.Processor.ProcessorId, stream))
                .Then(state => state.Logger.LogInformation("{ProcessorId} Stream {Stream} truncated", ctx.Processor.ProcessorId, state.Stream),
                    (ctx.Logger, Stream: stream));
        });
    }
}