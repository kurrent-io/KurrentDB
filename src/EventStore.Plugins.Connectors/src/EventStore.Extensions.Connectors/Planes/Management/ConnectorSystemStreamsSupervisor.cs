#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming.Processors;
using static EventStore.Connectors.ConnectorsSystemStreams;
using ManagementContracts = EventStore.Connectors.Management.Contracts.Events;

namespace EventStore.Connectors.Management;

/// <summary>
/// Responsible for configuring and deleting the system streams for a connector.
/// </summary>
public class ConnectorSystemStreamsSupervisor : ProcessingModule {
    public ConnectorSystemStreamsSupervisor(IPublisher client) {
        Process<ManagementContracts.ConnectorCreated>(
            async (evt, ctx) => {
                await client.SetStreamMetadata(
                    GetLeasesStream(evt.Connector.ConnectorId),
                    new StreamMetadata(maxCount: 1),
                    cancellationToken: ctx.CancellationToken
                );

                await client.SetStreamMetadata(
                    GetPositionsStream(evt.Connector.ConnectorId),
                    new StreamMetadata(maxCount: 10),
                    cancellationToken: ctx.CancellationToken
                );

                await client.SetStreamMetadata(
                    GetStateChangesStream(evt.Connector.ConnectorId),
                    new StreamMetadata(maxCount: 10),
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
                    GetStateChangesStream(evt.ConnectorId),
                    cancellationToken: ctx.CancellationToken
                );
            }
        );
    }
}