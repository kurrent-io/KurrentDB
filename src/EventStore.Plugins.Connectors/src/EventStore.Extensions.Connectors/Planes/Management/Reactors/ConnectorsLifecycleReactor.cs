#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using Kurrent.Surge.Protocol.Processors;
using Kurrent.Surge.Processors;
using Microsoft.Extensions.Logging;

using ProcessorContracts = Kurrent.Surge.Protocol.Processors;
using SharedContracts = Kurrent.Surge.Protocol;

namespace EventStore.Connectors.Management.Reactors;

sealed class ConnectorsLifecycleReactor : ProcessingModule {
    public ConnectorsLifecycleReactor(ConnectorsCommandApplication application) {
        Process<ProcessorStateChanged>(async (message, context) => {
            try {
                var cmd = new RecordConnectorStateChange {
                    ConnectorId  = message.Processor.ProcessorId,
                    FromState    = message.FromState.MapProcessorState(),
                    ToState      = message.ToState.MapProcessorState(),
                    ErrorDetails = message.Error.MapErrorDetails(),
                    Timestamp    = message.Metadata.Timestamp
                };

                await application.Handle(cmd, context.CancellationToken);
            }
            catch (Exception ex) {
                context.Logger.LogError(ex, "{ProcessorId} Failed to record connector state change", context.Processor.ProcessorId);
            }
        });
    }
}

[PublicAPI]
public static class ProcessorContractsMappers {
    public static ConnectorState MapProcessorState(this ProcessorContracts.ProcessorState source) =>
        source switch {
            ProcessorContracts.ProcessorState.Unspecified  => ConnectorState.Unknown,
            ProcessorContracts.ProcessorState.Activating   => ConnectorState.Activating,
            ProcessorContracts.ProcessorState.Running      => ConnectorState.Running,
            ProcessorContracts.ProcessorState.Deactivating => ConnectorState.Deactivating,
            ProcessorContracts.ProcessorState.Stopped      => ConnectorState.Stopped,
            _                                              => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    public static Error? MapErrorDetails(this SharedContracts.ErrorDetails? source) =>
        source is null ? null : new Error {
            Code    = source.Code,
            Message = source.ErrorMessage
        };
}