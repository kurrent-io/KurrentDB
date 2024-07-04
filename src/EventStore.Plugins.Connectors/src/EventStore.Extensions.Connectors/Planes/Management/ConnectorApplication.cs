using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.ConnectorDomainServices;
using static Eventuous.FuncServiceDelegates;
using ConnectorDeleted = EventStore.Connectors.Management.Contracts.Events.ConnectorDeleted;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorApplication : CommandService<ConnectorEntity> {
    static GetStreamNameFromCommand<dynamic> FromCommand() => cmd => new($"$connector-{cmd.ConnectorId}");

    public ConnectorApplication(ValidateConnectorSettings validateSettings, IEventStore store, TimeProvider time) : base(store) {
        On<CreateConnector>()
            .InState(ExpectedState.New)
            .GetStream(FromCommand())
            .Act(
                (_, _, cmd) => {
                    var result = validateSettings(cmd.Settings);

                    if (!result.IsValid)
                        InvalidConnectorSettings.Throw(cmd.ConnectorId, result.Errors);

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
                (connector, _, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    EnsureConnectorStopped(connector);

                    var result = validateSettings(cmd.Settings.ToDictionary());

                    if (!result.IsValid)
                        InvalidConnectorSettings.Throw(cmd.ConnectorId, result.Errors);

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
                (connector, _, _) => {
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
                (connector, _, _) => {
                    EnsureConnectorNotDeleted(connector);

                    if (connector.State
                        is ConnectorState.Running
                        or ConnectorState.Activating
                        or ConnectorState.Deactivating)
                        return [];

                    EnsureConnectorStopped(connector);

                    return [
                        new ConnectorActivating {
                            ConnectorId = connector.Id,
                            Settings    = { connector.CurrentRevision.Settings },
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<StopConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, _, _) => {
                    EnsureConnectorNotDeleted(connector);

                    if (connector.State
                        is ConnectorState.Stopped
                        or ConnectorState.Deactivating
                        or ConnectorState.Activating)
                        return [];

                    EnsureConnectorRunning(connector);

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
                (connector, _, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    EnsureConnectorStopped(connector);

                    return [
                        new ConnectorReset {
                            ConnectorId = connector.Id,
                            LogPosition = cmd.LogPosition,
                            Timestamp   = time.GetUtcNow().ToTimestamp()
                        }
                    ];
                }
            );

        On<RenameConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, _, cmd) => {
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
                (connector, _, cmd) => {
                    EnsureConnectorNotDeleted(connector);

                    // need to do all the state change validations here:
                    // Activating -> Running
                    // Activating -> Stopped
                    // Activating -> Failed
                    // Deactivating -> Stopped
                    // Deactivating -> Failed
                    // Running -> Stopped
                    // Running -> Failed

                    return cmd switch {
                        { ToState: ConnectorState.Running } => [
                            new ConnectorRunning {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp,
                                RecordedAt  = time.GetUtcNow().ToTimestamp()
                            }
                        ],
                        { ToState: ConnectorState.Stopped, ErrorDetails: null } => [
                            new ConnectorStopped {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp,
                                RecordedAt  = time.GetUtcNow().ToTimestamp()
                            }
                        ],
                        { ToState: ConnectorState.Stopped, ErrorDetails: not null } => [
                            new ConnectorFailed {
                                ConnectorId  = connector.Id,
                                ErrorDetails = cmd.ErrorDetails,
                                Timestamp    = cmd.Timestamp,
                                RecordedAt   = time.GetUtcNow().ToTimestamp()
                            }
                        ],
                        _ => []
                    };
                }
            );

        On<RecordConnectorPosition>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act((connector, _, cmd) => {
                EnsureConnectorNotDeleted(connector);
                EnsureCommittedLogPositionIsValid(connector, cmd.LogPosition);

                return [
                    new ConnectorPositionCommitted {
                        ConnectorId = connector.Id,
                        LogPosition = cmd.LogPosition,
                        Timestamp   = cmd.Timestamp,
                        RecordedAt  = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        return;

        static void EnsureConnectorNotDeleted(ConnectorEntity connector) {
            if (connector.IsDeleted)
                ConnectorDomainExceptions.ConnectorDeleted.Throw(connector);
        }

        static void EnsureConnectorStopped(ConnectorEntity connector) {
            if (connector.State is not ConnectorState.Stopped)
                throw new DomainException($"Connector {connector.Id} must be stopped. Current state: {connector.State}");
        }

        static void EnsureConnectorRunning(ConnectorEntity connector) {
            if (connector.State is not (ConnectorState.Running or ConnectorState.Activating))
                throw new DomainException($"Connector {connector.Id} must be running. Current state: {connector.State}");
        }

        static void EnsureCommittedLogPositionIsValid(ConnectorEntity connector, ulong? newLogPosition) {
            if (newLogPosition.GetValueOrDefault() < connector.LogPosition)
                throw new DomainException($"Connector {connector.Id} new log position {newLogPosition} precedes the actual {connector.LogPosition}");
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
            (state, evt) => state with {
                CurrentRevision = new ConnectorRevision {
                    Number    = evt.Revision,
                    Settings  = { evt.Settings },
                    CreatedAt = evt.Timestamp
                }
            }
        );

        On<ConnectorDeleted>(
            (state, _) => state with {
                IsDeleted = true
            }
        );

        On<ConnectorRenamed>(
            (state, evt) => state with {
                Name = evt.Name
            }
        );

        On<ConnectorActivating>(
            (state, evt) => state with {
                State = ConnectorState.Activating,
                CurrentRevision = new ConnectorRevision {
                    Number    = state.CurrentRevision.Number,
                    Settings  = { evt.Settings },
                    CreatedAt = evt.Timestamp
                }
            }
        );

        On<ConnectorDeactivating>(
            (state, _) => state with {
                State = ConnectorState.Deactivating
            }
        );

        On<ConnectorRunning>(
            (state, _) => state with {
                State = ConnectorState.Running
            }
        );

        On<ConnectorStopped>(
            (state, _) => state with {
                State = ConnectorState.Stopped
            }
        );

        On<ConnectorFailed>(
            (state, _) => state with {
                State = ConnectorState.Stopped
                // TODO JC: What do we want to do with the error details?
            }
        );

        On<ConnectorReset>(
            (state, evt) => state with {
                LogPosition = evt.LogPosition
            }
        );

        On<ConnectorPositionCommitted>(
            (state, evt) => state with {
                LogPosition = evt.LogPosition
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
    public ulong? LogPosition { get; init; } = null;

    /// <summary>
    /// Indicator if the connector is deleted.
    /// </summary>
    public bool IsDeleted { get; init; }
}

public static class ConnectorDomainServices {
    public delegate ValidationResult ValidateConnectorSettings(IDictionary<string, string?> settings);
}

public static class ConnectorDomainExceptions {
    public class ConnectorDeleted(string connectorId) : DomainException($"Connector {connectorId} is deleted") {
        public string ConnectorId { get; } = connectorId;

        public static void Throw(ConnectorEntity connector) =>
            throw new ConnectorDeleted(connector.Id);
    }

    public class OldLogPosition(string connectorId, ulong newPosition, ulong actualPosition)
        : DomainException($"Connector {connectorId} new log position {newPosition} is older than the actual {actualPosition}") {
        public string ConnectorId    { get; } = connectorId;
        public ulong  NewPosition    { get; } = newPosition;
        public ulong  ActualPosition { get; } = actualPosition;

        public static void Throw(string connectorId, ulong newPosition, ulong actualPosition) =>
            throw new OldLogPosition(connectorId, newPosition, actualPosition);
    }

    public class InvalidConnectorStateChange(string connectorId, ConnectorState currentState, ConnectorState requestedState)
        : DomainException($"Connector {connectorId} invalid state change from {currentState} to {requestedState} detected") {
        public string         ConnectorId    { get; } = connectorId;
        public ConnectorState CurrentState   { get; } = currentState;
        public ConnectorState RequestedState { get; } = requestedState;

        public static void Throw(string connectorId, ConnectorState currentState, ConnectorState requestedState) =>
            throw new InvalidConnectorStateChange(connectorId, currentState, requestedState);
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