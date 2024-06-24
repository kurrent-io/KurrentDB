using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DotNext.Collections.Generic;
using EventStore.Connect.Connectors;
using EventStore.Streaming;
using EventStore.Streaming.Consumers.Checkpoints;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Control.Activation;

public class ConnectorsTaskManagerException(string message, Exception? innerException = null) : Exception(message, innerException);

public class ConnectorProcessNotFoundException(ConnectorId connectorId) : ConnectorsTaskManagerException($"Connector {connectorId} not found") {
    public ConnectorId ConnectorId { get; } = connectorId;
}

/// <summary>
/// Manages the lifecycle of connector processes.
/// </summary>
[PublicAPI]
public class ConnectorsTaskManager : IAsyncDisposable {
    public ConnectorsTaskManager(CreateConnector createConnector) {
        CreateConnector = createConnector;

        Processes = new();

        LifetimeTokenSource = new CancellationTokenSource();
        LifetimeToken       = LifetimeTokenSource.Token;
    }

    public ConnectorsTaskManager(IConnectorFactory factory) : this(factory.CreateConnector) { }

    CreateConnector CreateConnector { get; }

    ConcurrentDictionary<ConnectorId, ConnectorProcess> Processes { get; }

    CancellationTokenSource LifetimeTokenSource { get; }
    CancellationToken       LifetimeToken       { get; }

    /// <summary>
    /// Initiates a connector process. This operation is performed on a best-effort basis and does not depend on the final state of the processor.
    /// </summary>
    /// <remarks>
    /// This method performs the following actions based on the state of the processor:<br/>
    /// - If the process does not exist, it creates and starts the process.<br/>
    /// - If the process exists but has a different revision, it stops the current process, creates a new one with the new configuration, and starts it.<br/>
    /// - If the process is deactivating, it waits for the process to deactivate, recreates the process, and starts it.<br/>
    /// - If the process is stopped, it recreates and starts the process.
    /// </remarks>
    public async Task<ConnectorProcessInfo> StartProcess(
        ConnectorId connectorId, int revision,
        IConfiguration configuration,
        CancellationToken stoppingToken = default
    ) {
        // if not exists, create and start process
        if (!Processes.TryGetValue(connectorId, out var process)) {
            Processes[connectorId] = process = new(CreateConnector(connectorId, configuration), revision);
            await process.Start(stoppingToken);
        }
        // if different revision, stop current process and create new with new configuration
        else if (process.Revision != revision) {
            await process.Stop();
            Processes[connectorId] = process = new(CreateConnector(connectorId, configuration), revision);
            await process.Start(stoppingToken);
        }
        // if deactivating, wait, recreate and start process
        else if (process.State == ConnectorState.Deactivating) {
            await Processes[connectorId]
                .Stop(); //TODO SS: we could wait if using the Stopped Task. now we can't. How to do this properly? loop on stopped state? arrrrghh

            Processes[connectorId] = process = new(CreateConnector(connectorId, configuration), revision);
            await process.Start(stoppingToken);
        }
        // if stopped, recreate and start process
        else if (process.State == ConnectorState.Stopped) {
            Processes[connectorId] = process = new(CreateConnector(connectorId, configuration), revision);
            await process.Start(stoppingToken);
        }

        return new(
            connectorId,
            process.Revision,
            process.State
            //process.IsFaulted(out var error) ? error : null
        );
    }

    public async Task<ConnectorProcessInfo> StopProcess(ConnectorId connectorId, CancellationToken cancellationToken = default) {
        if (!Processes.TryRemove(connectorId, out var process))
            throw new ConnectorProcessNotFoundException(connectorId);

        await process.Stop();

        return new(
            connectorId,
            process.Revision,
            process.State
            //process.IsFaulted(out var error) ? error : null
        );
    }

    public bool ProcessExists(ConnectorId connectorId, int? revision, [MaybeNullWhen(false)] out ConnectorProcessInfo info) {
        if (Processes.TryGetValue(connectorId, out var process) && (revision is null || process.Revision == revision)) {
            info = new(
                connectorId,
                process.Revision,
                process.State
                // process.IsFaulted(out var error) ? error : null
            );

            return true;
        }

        info = null;
        return false;
    }

    public bool ProcessExists(ConnectorId connectorId, [MaybeNullWhen(false)] out ConnectorProcessInfo info) =>
        ProcessExists(connectorId, null, out info);

    public List<ConnectorProcessInfo> GetProcesses() =>
        Processes.Values.Select(
            x => new ConnectorProcessInfo(
                ConnectorId.From(x.Connector.ConnectorId),
                x.Revision,
                x.State
                // x.Connector.IsFaulted(out var error) ? error : null
            )
        ).ToList();

    public async ValueTask DisposeAsync() {
        //TODO SS: Implement LifetimeTokenSource and LinkTo from dotnext to streamline stopping all processes

        await Parallel.ForEachAsync(
            Processes.Values,
            async (process, _) => {
                //TODO SS: log or throw?
                await process.Stop();
            }
        );

        Processes.Clear();
    }

    public record ConnectorProcessInfo(ConnectorId ConnectorId, int Revision, ConnectorState State, Exception? Error = null);

    [PublicAPI]
    record ConnectorProcess(IConnector Connector, int Revision) : IAsyncDisposable {
        public ConnectorState State => (ConnectorState)Connector.State;

        public Task Start(CancellationToken stoppingToken) =>
            Connector.Connect(stoppingToken);

        public async Task Stop() =>
            await Connector.DisposeAsync();

        // TODO SS: without the Stopped Task we dont know if the connector stopped without exceptions
        public bool IsFaulted(out Exception? error) {
            error = Connector.Stopped.Exception?.Flatten();
            return Connector.Stopped.IsFaulted;
        }

        public void Deconstruct(out IConnector connector, out int revision) {
            connector = Connector;
            revision  = Revision;
        }

        public ValueTask DisposeAsync() =>
            Connector.DisposeAsync();
    }
}