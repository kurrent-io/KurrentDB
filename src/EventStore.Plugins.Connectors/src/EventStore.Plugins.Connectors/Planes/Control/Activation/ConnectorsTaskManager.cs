using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using EventStore.IO.Connectors;
using EventStore.Streaming.Connectors;
using EventStore.Streaming.Processors;

namespace EventStore.Connectors.Control.Activation;

public class ConnectorsTaskManagerException(string message, Exception? innerException = null) : Exception(message, innerException);

public class ConnectorProcessNotFoundException(ConnectorId connectorId) : ConnectorsTaskManagerException($"Connector {connectorId} not found") {
    public ConnectorId ConnectorId { get; } = connectorId;
}

/// <summary>
/// Manages the lifecycle of connector processes.
/// </summary>
[PublicAPI]
public class ConnectorsTaskManager(CreateConnectorInstance createConnectorInstance) : IAsyncDisposable {
    public ConnectorsTaskManager(IConnectorsFactory factory) : this(factory.Create) { }

    CreateConnectorInstance CreateConnector { get; } = createConnectorInstance;

    ConcurrentDictionary<ConnectorId, ConnectorProcess> Processes { get; } = new();

    /// <summary>
    /// Initiates a connector process. This operation is performed on a best-effort basis and does not depend on the final state of the processor.
    /// </summary>
    /// <remarks>
    /// This method performs the following actions based on the state of the processor:<br/>
    /// - If the process does not exist, it creates and starts the process.<br/>
    /// - If the process exists but has a different revision, it stops the current process, creates a new one with the new settings, and starts it.<br/>
    /// - If the process is suspended, it resumes the process.<br/>
    /// - If the process is deactivating, it waits for the process to deactivate, recreates the process, and starts it.<br/>
    /// - If the process is stopped, it recreates and starts the process.
    /// </remarks>
    public async Task<ConnectorProcessInfo> StartProcess(ConnectorId connectorId, int revision, ConnectorSettings settings, CancellationToken cancellationToken = default) {
        // if not exists, create and start process
        if (!Processes.TryGetValue(connectorId, out var process)) {
            Processes[connectorId] = process = new(CreateConnector(connectorId, settings), revision);
            await process.Start();
        }
        // if different revision, stop current process and create new with new settings
        else if (process.Revision != revision) {
            await process.Stop();
            Processes[connectorId] = process = new(CreateConnector(connectorId, settings), revision);
            await process.Start();
        }
        // if suspended resume process
        else if (process.State == ProcessorState.Suspended) {
            await Processes[connectorId].Start();
        }
        // if deactivating, wait, recreate and start process
        else if (process.State == ProcessorState.Deactivating) {
            await Processes[connectorId].Processor.RunUntilDeactivated(CancellationToken.None);
            Processes[connectorId] = process = new(CreateConnector(connectorId, settings), revision);
            await process.Start();
        }
        // if stopped, recreate and start process
        else if (process.State == ProcessorState.Stopped) {
            Processes[connectorId] = process = new(CreateConnector(connectorId, settings), revision);
            await process.Start();
        }

        return new(
            connectorId, process.Revision, process.State,
            process.IsFaulted(out var error) ? error : null
        );
    }

    public async Task<ConnectorProcessInfo> StopProcess(ConnectorId connectorId, CancellationToken cancellationToken = default) {
        if (!Processes.TryRemove(connectorId, out var process))
            throw new ConnectorProcessNotFoundException(connectorId);

        await process.Stop();

        return new(
            connectorId, process.Revision, process.State,
            process.IsFaulted(out var error) ? error : null
        );
    }

    public bool ProcessExists(ConnectorId connectorId, int? revision, [MaybeNullWhen(false)] out ConnectorProcessInfo info) {
        if (Processes.TryGetValue(connectorId, out var process) && (revision is null || process.Revision == revision)) {
            info = new(
                connectorId, process.Revision, process.State,
                process.IsFaulted(out var error) ? error : null
            );
            return true;
        }

        info = null;
        return false;
    }

    public bool ProcessExists(ConnectorId connectorId, [MaybeNullWhen(false)] out ConnectorProcessInfo info) =>
        ProcessExists(connectorId, null, out info);

    public List<ConnectorProcessInfo> GetProcesses() =>
        Processes.Select(
            kvp => new ConnectorProcessInfo(
                kvp.Key, kvp.Value.Revision, kvp.Value.State,
                kvp.Value.IsFaulted(out var error) ? error : null
            )
        ).ToList();

    public async ValueTask DisposeAsync() {
        await Parallel.ForEachAsync(
            Processes.Values,
            async (process, _) => {
                //TODO SS: log or throw?
                await process.Stop();
            }
        );

        Processes.Clear();
    }

    public record ConnectorProcessInfo(ConnectorId ConnectorId, int Revision, ProcessorState State, Exception? Error = null);

    [PublicAPI]
    record ConnectorProcess(IProcessor Processor, int Revision) {
        public ProcessorState State => Processor.State;

        public Task Start() =>
            Processor.State is ProcessorState.Suspended
                ? Processor.Resume()
                : Processor.Activate(CancellationToken.None);

        public Task Stop() =>
            Processor.State is ProcessorState.Stopped or ProcessorState.Deactivating
                ? Processor.Stopped
                : Processor.Deactivate();

        public Task Suspend() =>
            Processor.State is ProcessorState.Running
                ? Processor.Suspend()
                : Task.CompletedTask;

        public bool IsFaulted(out Exception? error) {
            error = Processor.Stopped.Exception?.Flatten();
            return Processor.Stopped.IsFaulted;
        }
    }
}