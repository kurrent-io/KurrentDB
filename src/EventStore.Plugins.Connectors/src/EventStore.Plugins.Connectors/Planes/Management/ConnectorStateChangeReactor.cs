#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Streaming.Processors;
using StreamingContracts = EventStore.Streaming.Contracts.Processors;
using ManagementContracts = EventStore.Connectors.Management.Contracts.Commands;

namespace EventStore.Connectors.Management;

public class ConnectorStateChangeReactor : ProcessingModule {
    public ConnectorStateChangeReactor() =>
        Process<StreamingContracts.ProcessorStateChanged>(
            (evt, ctx) => ctx.Output(
                new ManagementContracts.RecordConnectorStateChange {
                    ConnectorId = evt.Processor.ProcessorName,
                    FromState   = Contracts.ConnectorState.Unknown, // evt.PreviousState,
                    ToState     = Contracts.ConnectorState.Unknown, // evt.CurrentState,
                    // ErrorDetails = evt.Error,
                    Timestamp = evt.Metadata.Timestamp
                }
            )
        );
}

public class ConnectorStateChangeReactor2 : RecordHandler<StreamingContracts.ProcessorStateChanged> {
    public override async Task Process(StreamingContracts.ProcessorStateChanged evt, RecordContext context) =>
        context.Output(
            new ManagementContracts.RecordConnectorStateChange {
                ConnectorId = evt.Processor.ProcessorName,
                FromState   = Contracts.ConnectorState.Unknown, // evt.PreviousState,
                ToState     = Contracts.ConnectorState.Unknown, // evt.CurrentState,
                //ErrorDetails = evt.Error,
                Timestamp = evt.Metadata.Timestamp
            }
        );
}
