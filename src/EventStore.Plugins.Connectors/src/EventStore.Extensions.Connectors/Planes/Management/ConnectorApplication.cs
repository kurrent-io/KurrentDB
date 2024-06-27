using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using static EventStore.Connectors.Management.ConnectorDomainServices;
using static Eventuous.FuncServiceDelegates;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorApplication : CommandService<ConnectorEntity> {
    static GetStreamNameFromCommand<dynamic> FromCommand() => cmd => new($"$connector-{cmd.ConnectorId}");

    public ConnectorApplication(
        ValidateConnectorSettings validateSettings, IEventStore store, TimeProvider time
    ) : base(store) {
        On<CreateConnector>()
            .InState(ExpectedState.New)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    var result = validateSettings(cmd.Settings.ToDictionary());

                    if (!result.IsValid)
                        ConnectorDomainExceptions.InvalidConnectorSettings.Throw(cmd.ConnectorId, result.Errors);

                    return [
                        new ConnectorCreated {
                            ConnectorId = cmd.ConnectorId,
                            Name        = cmd.Name,
                            Settings    = { cmd.Settings },
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<ReconfigureConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);

                    var result = validateSettings(cmd.Settings.ToDictionary());

                    if (!result.IsValid)
                        ConnectorDomainExceptions.InvalidConnectorSettings.Throw(cmd.ConnectorId, result.Errors);

                    return [
                        new ConnectorReconfigured {
                            ConnectorId = connector.Id,
                            Revision    = connector.CurrentRevision.Number + 1,
                            Settings    = { cmd.Settings },
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<DeleteConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    EnsureConnectorStopped(connector);

                    return [
                        new ConnectorDeleted {
                            ConnectorId = connector.Id,
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<StartConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    EnsureConnectorStopped(connector);

                    if (connector.State
                        is ConnectorState.Running
                        or ConnectorState.Activating
                        or ConnectorState.Deactivating)
                        return [];

                    return [
                        new ConnectorActivating {
                            ConnectorId = connector.Id,
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<StopConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    EnsureConnectorRunning(connector);

                    if (connector.State
                        is ConnectorState.Stopped
                        or ConnectorState.Deactivating
                        or ConnectorState.Activating)
                        return [];

                    return [
                        new ConnectorDeactivating {
                            ConnectorId = connector.Id,
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<ResetConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    EnsureConnectorStopped(connector);

                    return [
                        new ConnectorReset {
                            ConnectorId = connector.Id,
                            Positions   = { cmd.Positions },
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<RenameConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);

                    if (connector.Name == cmd.Name)
                        return [];

                    return [
                        new ConnectorRenamed {
                            ConnectorId = connector.Id,
                            Name        = cmd.Name,
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<RecordConnectorStateChange>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);

                    return cmd switch {
                        { ToState: ConnectorState.Running } => [
                            new ConnectorRunning {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp
                            }
                        ],
                        { ToState: ConnectorState.Stopped, ErrorDetails: null } => [
                            new ConnectorStopped {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp
                            }
                        ],
                        { ToState: ConnectorState.Stopped, ErrorDetails: not null } => [
                            new ConnectorFailed {
                                ConnectorId  = connector.Id,
                                ErrorDetails = cmd.ErrorDetails,
                                Timestamp    = cmd.Timestamp
                            }
                        ],
                        _ => []
                    };
                }
            );

        On<RecordConnectorPositions>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);

                    // TODO SS: Should we check if the positions are older than the last committed positions?

                    return [
                        new ConnectorPositionsCommitted {
                            ConnectorId = connector.Id,
                            Positions   = { cmd.Positions },
                            CommittedAt = cmd.Timestamp
                        }
                    ];
                }
            );

        return;

        static void EnsureConnectorNotDeleted(ConnectorEntity connector) {
            if (connector.IsDeleted)
                ConnectorDomainExceptions.ConnectorDeleted.Throw(connector);
        }

        static void EnsureConnectorStopped(ConnectorEntity connector) {
            if (connector.State is not ConnectorState.Stopped)
                throw new DomainException(
                    $"Connector {connector.Id} must be stopped. Current state: {connector.State}"
                );
        }

        static void EnsureConnectorRunning(ConnectorEntity connector) {
            if (connector.State is not (ConnectorState.Running or ConnectorState.Activating))
                throw new DomainException(
                    $"Connector {connector.Id} must be running. Current state: {connector.State}"
                );
        }
    }
}

public record ConnectorEntity : State<ConnectorEntity> {
    public ConnectorEntity() {
        On<ConnectorCreated>(
            (state, evt) => state with {
                Id = evt.ConnectorId,
                Name = evt.Name,
                CurrentRevision = new ConnectorRevision {
                    Number    = 0,
                    Settings  = { evt.Settings },
                    CreatedAt = evt.Timestamp
                }
            }
        );

        On<ConnectorReconfigured>(
            (state, evt) =>
                state with {
                    CurrentRevision = new ConnectorRevision {
                        Number    = evt.Revision,
                        Settings  = { evt.Settings },
                        CreatedAt = evt.Timestamp
                    }
                }
        );

        On<ConnectorDeleted>(
            (state, evt) =>
                state with {
                    IsDeleted = true
                }
        );

        On<ConnectorRenamed>(
            (state, evt) =>
                state with {
                    Name = evt.Name
                }
        );

        On<ConnectorActivating>(
            (state, evt) =>
                state with {
                    State = ConnectorState.Activating,
                    CurrentRevision = new ConnectorRevision {
                        Number    = state.CurrentRevision.Number,
                        Settings  = { evt.Settings },
                        CreatedAt = evt.Timestamp
                    }
                }
        );

        On<ConnectorDeactivating>(
            (state, evt) => state with {
                State = ConnectorState.Deactivating
            }
        );

        On<ConnectorRunning>(
            (state, evt) =>
                state with {
                    State = ConnectorState.Running
                }
        );

        On<ConnectorStopped>(
            (state, evt) =>
                state with {
                    State = ConnectorState.Stopped
                }
        );

        On<ConnectorFailed>(
            (state, evt) =>
                state with {
                    State = ConnectorState.Stopped
                    // TODO JC: What do we want to do with the error details?
                }
        );

        On<ConnectorReset>(
            (state, evt) =>
                state with {
                    // TODO JC: What do we want to do here?
                }
        );

        On<ConnectorPositionsCommitted>(
            (state, evt) =>
                state with {
                    LogPosition = evt.Positions[^1].LogPosition
                }
        );
    }

    /// <summary>
    /// Unique identifier of the connector.
    /// </summary>
    public string Id { get; init; } = Guid.Empty.ToString();

    /// <summary>
    /// Name of the connector.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The current connector settings.
    /// </summary>
    public ConnectorRevision CurrentRevision { get; init; } = new();

    /// <summary>
    /// The state of the connector.
    /// </summary>
    public ConnectorState State { get; init; } = ConnectorState.Unknown;

    /// <summary>
    /// The current log position of the connector.
    /// </summary>
    public ulong LogPosition { get; init; }

    /// <summary>
    /// Indicator if the connector is deleted.
    /// </summary>
    public bool IsDeleted { get; init; }
}

public static class ConnectorDomainServices {
    public delegate ValidationResult ValidateConnectorSettings(Dictionary<string, string?> settings);
}

public static class ConnectorDomainExceptions {
    public class ConnectorDeleted(string connectorId)
        : DomainException($"Connector {connectorId} is deleted") {
        public string ConnectorId { get; } = connectorId;

        public static void Throw(ConnectorEntity connector) =>
            throw new ConnectorDeleted(connector.Id);
    }

    public class ConnectorInvalidState(string connectorId, ConnectorState currentState, ConnectorState requestedState)
        : DomainException(
            $"Connector {connectorId} invalid state change from {currentState} to {requestedState} detected"
        ) {
        public string         ConnectorId    { get; } = connectorId;
        public ConnectorState CurrentState   { get; } = currentState;
        public ConnectorState RequestedState { get; } = requestedState;

        public static void Throw(string connectorId, ConnectorState currentState, ConnectorState requestedState) =>
            throw new ConnectorInvalidState(connectorId, currentState, requestedState);
    }

    public class InvalidConnectorSettings(string connectorId, Dictionary<string, string[]> errors)
        : DomainException($"Connector {connectorId} invalid settings detected") {
        public string                        ConnectorId { get; } = connectorId;
        public IDictionary<string, string[]> Errors      { get; } = errors;

        public static void Throw(string connectorId, List<ValidationFailure> failures) {
            var errors = failures
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.ErrorMessage).ToArray()
                );

            throw new InvalidConnectorSettings(connectorId, errors);
        }
    }
}