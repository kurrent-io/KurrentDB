#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Connectors.Management.Contracts;
using EventStore.Streaming.Processors;
using Eventuous;
using Microsoft.Extensions.Logging;
using StreamingContracts = EventStore.Streaming.Contracts.Processors;
using ManagementContracts = EventStore.Connectors.Management.Contracts.Commands;

namespace EventStore.Connectors.Management;

public class ConnectorStateChangeReactor : ProcessingModule {
    public ConnectorStateChangeReactor(ICommandService<ConnectorEntity> application) {
        Process<StreamingContracts.ProcessorStateChanged>(
            (evt, ctx) => {
                try {
                    application.Handle(
                        new ManagementContracts.RecordConnectorStateChange {
                            ConnectorId = evt.Processor.ProcessorId,
                            FromState   = ConnectorState.Unknown, // evt.PreviousState,
                            ToState     = ConnectorState.Unknown, // evt.CurrentState,
                            // ErrorDetails = evt.Error, fix error details
                            Timestamp = evt.Metadata.Timestamp
                        },
                        ctx.CancellationToken
                    );
                }
                catch (Exception e) {
                    ctx.Logger.LogError(e, "Failed to record connector state change.");
                }
            }
        );
    }
}