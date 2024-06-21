#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Streaming;
using EventStore.Streaming.Processors;
using StreamingContracts = EventStore.Streaming.Contracts.Processors;
using ManagementContracts = EventStore.Connectors.Management.Contracts.Commands;

namespace EventStore.Connectors.Management;

public delegate StreamId GetConnectorStream(string connectorId);

// static StreamId GetConnectorStream(string connectorId) => StreamId.From($"$connectors/{connectorId}");

public class ConnectorStateChangeReactor : ProcessingModule {
    public ConnectorStateChangeReactor(GetConnectorStream getConnectorStream) {
        Process<StreamingContracts.ProcessorStateChanged>(
            (evt, ctx) => ctx.Output(
                new ManagementContracts.RecordConnectorStateChange {
                    ConnectorId = evt.Processor.ProcessorId,
                    FromState   = Contracts.ConnectorState.Unknown, // evt.PreviousState,
                    ToState     = Contracts.ConnectorState.Unknown, // evt.CurrentState,
                    // ErrorDetails = evt.Error,
                    Timestamp = evt.Metadata.Timestamp
                },
                getConnectorStream(evt.Processor.ProcessorId)
            )
        );
    }
}

public class ConnectorStateChangeReactor2(GetConnectorStream getConnectorStream) : RecordHandler<StreamingContracts.ProcessorStateChanged> {
    public override async Task Process(StreamingContracts.ProcessorStateChanged evt, RecordContext context) =>
        context.Output(
            new ManagementContracts.RecordConnectorStateChange {
                ConnectorId = evt.Processor.ProcessorId,
                FromState   = Contracts.ConnectorState.Unknown, // evt.PreviousState,
                ToState     = Contracts.ConnectorState.Unknown, // evt.CurrentState,
                //ErrorDetails = evt.Error,
                Timestamp = evt.Metadata.Timestamp
            },
            getConnectorStream(evt.Processor.ProcessorId)
        );
}