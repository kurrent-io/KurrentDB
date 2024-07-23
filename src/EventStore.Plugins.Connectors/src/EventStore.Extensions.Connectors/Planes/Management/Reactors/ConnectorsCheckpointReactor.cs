using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Streaming.Contracts.Consumers;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Reactors;

class ConnectorsCheckpointReactor(ConnectorApplication application) : RecordHandler<Checkpoint> {
    public override async Task Process(Checkpoint message, RecordContext context) {
        try {
            var cmd = new RecordConnectorPosition {
                ConnectorId = message.ConsumerId,
                LogPosition = message.LogPosition,
                Timestamp   = message.Timestamp
            };

            await application.Handle(cmd, context.CancellationToken);
        }
        catch (Exception ex) {
            context.Logger.LogError(ex, "Failed to record connector checkpoint position.");
        }
    }
}