using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming.Connectors.Sinks;
using Eventuous;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using static EventStore.Connectors.Management.ConnectorDomainServices;
using static Eventuous.FuncServiceDelegates;
using ConfigurationExtensions = EventStore.Streaming.ConfigurationExtensions;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorApplication : FunctionalCommandService<ConnectorEntity> {
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

                    var config = new ConfigurationBuilder()
                        .AddInMemoryCollection(cmd.Settings)
                        .Build();

                    var sinkOptions = ConfigurationExtensions.GetRequiredOptions<SinkOptions>(config);

                    return [
                        new ConnectorCreated {
                            ConnectorId  = cmd.ConnectorId,
                            Name         = cmd.Name,
                            Type         = ConnectorType.Sink,
                            InstanceType = sinkOptions.ConnectorTypeName,
                            Settings     = { cmd.Settings },
                            Timestamp    = time.GetUtcNow().ToTimestamp()
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

                    return [
                        new ConnectorReset {
                            ConnectorId = connector.Id,
                            Positions = { cmd.Positions },
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
                        { ToState: ConnectorState.Stopped } => [
                            new ConnectorStopped {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp
                            }
                        ],
                        { ToState: ConnectorState.Failed, ErrorDetails: not null } => [
                            new ConnectorFailed {
                                ConnectorId  = connector.Id,
                                ErrorDetails = cmd.ErrorDetails,
                                Timestamp    = cmd.Timestamp
                            }
                        ],
                        _ => []
                    };

                    // TODO JC: Are there any other states to handle?
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
            if (connector.System.IsDeleted)
                ConnectorDomainExceptions.ConnectorDeleted.Throw(connector);
        }

        static void EnsureConnectorStopped(ConnectorEntity connector) {
            if (connector.State is not (ConnectorState.Stopped or ConnectorState.Failed))
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
            (state, evt) => {
                var initialRevision = new ConnectorRevision {
                    Number    = 0,
                    Settings  = { evt.Settings },
                    CreatedAt = evt.Timestamp
                };

                return state with {
                    Id = evt.ConnectorId,
                    Name = evt.Name,
                    Type = evt.Type,
                    Revisions = new Dictionary<int, ConnectorRevision> {
                        { initialRevision.Number, initialRevision }
                    },
                    CurrentRevision = initialRevision,
                    System = new SystemData {
                        CreatedAt      = evt.Timestamp,
                        LastModifiedAt = evt.Timestamp
                    }
                };
            }
        );

        On<ConnectorReconfigured>(
            (state, evt) => {
                var newState = state with {
                    CurrentRevision = new ConnectorRevision {
                        Number    = evt.Revision,
                        Settings  = { evt.Settings },
                        CreatedAt = evt.Timestamp
                    }
                };

                newState.Revisions[evt.Revision] = newState.CurrentRevision;
                newState.System.LastModifiedAt   = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorDeleted>(
            (state, evt) => {
                var newState = state with {
                    // TODO JC: What do we want to do here?
                };

                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorRenamed>(
            (state, evt) => {
                var newState = state with { Name = evt.Name };
                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorActivating>(
            (state, evt) => {
                var newState = state with { State = ConnectorState.Activating };
                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorDeactivating>(
            (state, evt) => {
                var newState = state with { State = ConnectorState.Deactivating };
                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorRunning>(
            (state, evt) => {
                var newState = state with { State = ConnectorState.Running };
                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorStopped>(
            (state, evt) => {
                var newState = state with { State = ConnectorState.Stopped };
                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorFailed>(
            (state, evt) => {
                var newState = state with { State = ConnectorState.Failed };
                newState.System.LastModifiedAt = evt.Timestamp;

                // TODO JC: What do we want to do with evt.ErrorDetails?

                return newState;
            }
        );

        On<ConnectorReset>(
            (state, evt) => {
                var newState = state with {
                    // TODO JC: What do we want to do here?
                };

                newState.System.LastModifiedAt = evt.Timestamp;

                return newState;
            }
        );

        On<ConnectorPositionsCommitted>(
            (state, evt) => {
                var newState = state with { Positions = evt.Positions.Concat(state.Positions).ToList() };
                state.System.LastModifiedAt = evt.CommittedAt;

                return newState;
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
    /// The type of the connector.
    /// </summary>
    public ConnectorType Type { get; init; } = ConnectorType.Unspecified;

    /// <summary>
    /// Revision history of the connector settings.
    /// </summary>
    public Dictionary<int, ConnectorRevision> Revisions { get; init; } = [];

    /// <summary>
    /// The current connector settings.
    /// </summary>
    public ConnectorRevision CurrentRevision { get; init; } = new();

    /// <summary>
    /// The state of the connector.
    /// </summary>
    public ConnectorState State { get; init; } = ConnectorState.Unknown;

    /// <summary>
    /// A list of the last recorded positions of the connector.
    /// </summary>
    public List<RecordPosition> Positions { get; init; } = [];

    /// <summary>
    /// Metadata pertaining to the creation and last modification of the connector.
    /// </summary>
    public SystemData System { get; init; } = new();
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

    public class InvalidConnectorSettings(string connectorId, IDictionary<string, string[]> errors)
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