// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Connectors.Management.Contracts;
using KurrentDB.Connectors.Management.Contracts.Commands;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Interceptors;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Interceptors;
using KurrentDB.Connectors.Planes.Management;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Connectors.Planes.Control;

[PublicAPI]
class ConnectorsLifecycleInterceptor : InterceptorModule {
    public ConnectorsLifecycleInterceptor(ConnectorsCommandApplication application, string? name = null) : base(name) {
        On<ProcessorStateChanged>(async (evt, ctx) => {
            try {
                var cmd = new RecordConnectorStateChange {
                    ConnectorId  = evt.Processor.ProcessorId,
                    FromState    = evt.FromState.MapProcessorState(),
                    ToState      = evt.ToState.MapProcessorState(),
                    ErrorDetails = evt.Error.MapErrorDetails(),
                    Timestamp    = evt.Timestamp.ToTimestamp()
                };

                await application.Handle(cmd, ctx.CancellationToken);
            }
            catch (Exception ex) {
                ctx.Logger.LogError(ex, "{ProcessorId} Failed to record connector state change", evt.Processor.ProcessorId);
            }
        });
    }
}

public static class ConnectorsLifecycleMaps {
    public static ConnectorState MapProcessorState(this ProcessorState source) =>
        source switch {
            ProcessorState.Unspecified  => ConnectorState.Unknown,
            ProcessorState.Activating   => ConnectorState.Activating,
            ProcessorState.Running      => ConnectorState.Running,
            ProcessorState.Deactivating => ConnectorState.Deactivating,
            ProcessorState.Stopped      => ConnectorState.Stopped,
            _                           => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown state")
        };

    public static Contracts.Error? MapErrorDetails(this Exception? source) =>
        source is null ? null : new() {
            Code    = source.GetType().Name,
            Message = source.ToString()
        };
}
