#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming.Processors;
using static EventStore.Connectors.ConnectorsSystemConventions;
using ManagementContracts = EventStore.Connectors.Management.Contracts.Events;

namespace EventStore.Connectors.Management;

[PublicAPI]
public record ConnectorSystemStreamsOptions {
    public SystemStreamOptions Leases       { get; init; } = new(MaxCount: 1);
    public SystemStreamOptions Positions    { get; init; } = new(MaxCount: 5);
    public SystemStreamOptions StateChanges { get; init; } = new(MaxCount: 10);
}

public record SystemStreamOptions(int? MaxCount = null, TimeSpan? MaxAge = null);

/// <summary>
/// Responsible for configuring and deleting the system streams for a connector.
/// </summary>
public class ConnectorSystemStreamsSupervisor : ProcessingModule {
    public ConnectorSystemStreamsSupervisor(IPublisher client, ConnectorSystemStreamsOptions options) {
        Process<ManagementContracts.ConnectorCreated>(
            async (evt, ctx) => {
                await client.SetStreamMetadata(
                    GetLeasesStream(evt.ConnectorId),
                    new StreamMetadata(maxCount: options.Leases.MaxCount, maxAge: options.Leases.MaxAge),
                    cancellationToken: ctx.CancellationToken
                );

                await client.SetStreamMetadata(
                    GetPositionsStream(evt.ConnectorId),
                    new StreamMetadata(maxCount: options.Positions.MaxCount, maxAge: options.Positions.MaxAge),
                    cancellationToken: ctx.CancellationToken
                );

                await client.SetStreamMetadata(
                    GetLifetimeStream(evt.ConnectorId),
                    new StreamMetadata(maxCount: options.StateChanges.MaxCount, maxAge: options.StateChanges.MaxAge),
                    cancellationToken: ctx.CancellationToken
                );
            }
        );

        Process<ManagementContracts.ConnectorDeleted>(
            async (evt, ctx) => {
                // it is possible that we cannot do it here
                // since we are consuming events from this same stream.
                // must test this scenario and basically create a cleanup stream to do this.

                await client.SoftDeleteStream(
                    GetManagementStream(evt.ConnectorId),
                    cancellationToken: ctx.CancellationToken
                );

                await client.DeleteStream(
                    GetLeasesStream(evt.ConnectorId),
                    cancellationToken: ctx.CancellationToken
                );

                await client.DeleteStream(
                    GetPositionsStream(evt.ConnectorId),
                    cancellationToken: ctx.CancellationToken
                );

                await client.DeleteStream(
                    GetLifetimeStream(evt.ConnectorId),
                    cancellationToken: ctx.CancellationToken
                );
            }
        );
    }
}