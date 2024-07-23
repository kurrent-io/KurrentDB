#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming.Processors;
using static EventStore.Connectors.ConnectorsSystemConventions.Streams;
using ManagementContracts = EventStore.Connectors.Management.Contracts.Events;

namespace EventStore.Connectors.Management.Reactors;

[PublicAPI]
public record ConnectorSystemStreamsOptions {
    public SystemStreamOptions Leases      { get; init; } = new(MaxCount: 3);
    public SystemStreamOptions Checkpoints { get; init; } = new(MaxCount: 3);
    public SystemStreamOptions Lifetime    { get; init; } = new(MaxCount: 5);
}

public record SystemStreamOptions(int? MaxCount = null, TimeSpan? MaxAge = null);

/// <summary>
/// Responsible for configuring and deleting the system streams for a connector.
/// </summary>
public class ConnectorsStreamSupervisor : ProcessingModule {
    public ConnectorsStreamSupervisor(IPublisher client, ConnectorSystemStreamsOptions? options = null) {
        options ??= new();

        Process<ManagementContracts.ConnectorCreated>(
            async (evt, ctx) => {
                await client.SetStreamMetadata(
                    GetLeasesStream(evt.ConnectorId),
                    new StreamMetadata(maxCount: options.Leases.MaxCount, maxAge: options.Leases.MaxAge),
                    cancellationToken: ctx.CancellationToken
                );

                await client.SetStreamMetadata(
                    GetCheckpointsStream(evt.ConnectorId),
                    new StreamMetadata(maxCount: options.Checkpoints.MaxCount, maxAge: options.Checkpoints.MaxAge),
                    cancellationToken: ctx.CancellationToken
                );

                await client.SetStreamMetadata(
                    GetLifecycleStream(evt.ConnectorId),
                    new StreamMetadata(maxCount: options.Lifetime.MaxCount, maxAge: options.Lifetime.MaxAge),
                    cancellationToken: ctx.CancellationToken
                );
            }
        );

        Process<ManagementContracts.ConnectorDeleted>(
            async (evt, ctx) => {
                // it is possible that we cannot do it here
                // since we are consuming events from this same stream.
                // must test this scenario and basically create a cleanup stream to do this.

                await client.SoftDeleteStream(GetManagementStream(evt.ConnectorId), ctx.CancellationToken);
                await client.SoftDeleteStream(GetLeasesStream(evt.ConnectorId), ctx.CancellationToken);
                await client.SoftDeleteStream(GetCheckpointsStream(evt.ConnectorId), ctx.CancellationToken);
                await client.SoftDeleteStream(GetLifecycleStream(evt.ConnectorId), ctx.CancellationToken);
            }
        );
    }
}