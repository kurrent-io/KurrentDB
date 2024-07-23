#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Streaming.Contracts.Processors;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Reactors;

class ConnectorsLifecycleReactor(ConnectorApplication application) : RecordHandler<ProcessorStateChanged> {
    public override async Task Process(ProcessorStateChanged message, RecordContext context) {
        try {
            var cmd = new RecordConnectorStateChange {
                ConnectorId = message.Processor.ProcessorId,
                FromState   = (ConnectorState)message.FromState.GetHashCode(),
                ToState     = (ConnectorState)message.ToState.GetHashCode(),
                ErrorDetails = message.Error is not null
                    ? new Error {
                        Code    = message.Error.Code,
                        Message = message.Error.ErrorMessage
                    }
                    : null,
                Timestamp = message.Metadata.Timestamp
            };

            await application.Handle(cmd, context.CancellationToken);
        }
        catch (Exception ex) {
            context.Logger.LogError(ex, "Failed to record connector state change.");
        }
    }
}