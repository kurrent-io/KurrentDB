using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;
using Google.Protobuf.WellKnownTypes;
using static EventStore.Connectors.Management.ConnectorDomainServices;
using static Eventuous.FuncServiceDelegates;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorApplication : FunctionalCommandService<ConnectorEntity> {
    static GetStreamNameFromCommand<dynamic> FromCommand() => cmd => new($"$connector-{cmd.ConnectorId}");

    public ConnectorApplication(ValidateConnectorSettings validateSettings, IEventStore store, TimeProvider time) : base(store) {
        On<CreateConnector>()
            .InState(ExpectedState.New)
            .GetStream(FromCommand())
            .ActAsync(
                async (_, _, cmd, ct) => {
                    var result = await validateSettings(cmd.InstanceType, cmd.Settings.ToDictionary(), ct);

                    if (!result.Valid)
                        ConnectorDomainExceptions.InvalidConnectorSettings
                            .Throw(cmd.ConnectorId, result.InvalidSettings);

                    var now = time.GetUtcNow().ToTimestamp();

                    var revision = new ConnectorRevision {
                        Number    = 0,
                        Settings  = { cmd.Settings },
                        CreatedAt = now
                    };

                    return [
                        new ConnectorCreated {
                            Connector = new() {
                                ConnectorId     = cmd.ConnectorId,
                                Name            = cmd.Name,
                                ConnectorType   = cmd.ConnectorType,
                                InstanceType    = cmd.InstanceType,
                                CurrentRevision = revision.Number,
                                Revisions       = { { revision.Number, revision } },
                                State           = ConnectorState.Stopped,
                                // LastPositions = { Array.Empty<RecordPosition>() },
                                System = new() {
                                    CreatedAt      = revision.CreatedAt,
                                    LastModifiedAt = revision.CreatedAt
                                }
                            }
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
                            ConnectorId = connector.ConnectorId,
                            DeletedAt   = time.GetUtcNow().ToTimestamp()
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
                    
                    EnsureConnectorStopped(connector); // or should we be nice?

                    if (connector.State
                        is ConnectorState.Running
                        or ConnectorState.Activating
                        or ConnectorState.Deactivating)
                        return [];

                    return [
                        new ConnectorActivating {
                            ConnectorId = connector.ConnectorId,
                            Revision    = connector.CurrentRevision.Number,
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
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);
                    
                    EnsureConnectorRunning(connector);  // or should we be nice?

                    if (connector.State
                        is ConnectorState.Stopped
                        or ConnectorState.Deactivating
                        or ConnectorState.Activating)
                        return [];

                    return [
                        new ConnectorDeactivating {
                            ConnectorId = connector.ConnectorId,
                            Revision    = connector.CurrentRevision.Number,
                            Settings    = { connector.CurrentRevision.Settings },
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
                            ConnectorId = connector.ConnectorId,
                            Name        = cmd.Name,
                            RenamedAt   = time.GetUtcNow().ToTimestamp()
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
                    
                    // some quick and dirty state transitions

                    if (cmd.ToState == ConnectorState.Running) {
                        return [
                            new ConnectorRunning {
                                ConnectorId = connector.ConnectorId,
                                Revision    = connector.CurrentRevision.Number,
                                // Settings    = { connector.CurrentRevision.Settings }, // do we need this?
                                Timestamp   = cmd.Timestamp
                            }
                        ];
                    }

                    if (cmd.ToState == ConnectorState.Stopped) {
                        return cmd.ErrorDetails is null
                            ? [
                                new ConnectorStopped {
                                    ConnectorId  = connector.ConnectorId,
                                    Revision     = connector.CurrentRevision.Number,
                                    // Settings     = { connector.CurrentRevision.Settings }, // do we need this?
                                    Timestamp    = cmd.Timestamp
                                }
                            ]
                            : [
                                new ConnectorFailed {
                                    ConnectorId  = connector.ConnectorId,
                                    Revision     = connector.CurrentRevision.Number, 
                                    // Settings     = { connector.CurrentRevision.Settings }, // do we need this?
                                    ErrorDetails = cmd.ErrorDetails,
                                    Timestamp    = cmd.Timestamp
                                }
                            ];
                    }
                    
                    // // I think failed is not a direct state, but a stopped state transition with an error
                    // if (cmd.ToState == ConnectorState.Failed) {
                    //     return [
                    //         new ConnectorFailed {
                    //             ConnectorId  = connector.ConnectorId,
                    //             Revision     = connector.CurrentRevision.Number,
                    //             Settings     = { connector.CurrentRevision.Settings }, // do we need this?
                    //             ErrorDetails = cmd.ErrorDetails, 
                    //             Timestamp    = cmd.Timestamp
                    //         }
                    //     ];
                    // }

                    // skip the rest of the states for now

                    return [];
                }
            );

        On<RecordConnectorPositions>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, events, cmd) => {
                    EnsureConnectorNotDeleted(connector);

                    // TODO SS: check if the positions are older than the last committed positions?

                    return [
                        new ConnectorPositionsCommitted {
                            ConnectorId = connector.ConnectorId,
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
                throw new DomainException($"Connector {connector.ConnectorId} must be stopped");
        }
        
        static void EnsureConnectorRunning(ConnectorEntity connector) {
            if (connector.State is not (ConnectorState.Running or ConnectorState.Activating))
                throw new DomainException($"Connector {connector.ConnectorId} must be running");
        }
    }
}

public record ConnectorEntity : State<ConnectorEntity> {
    public ConnectorEntity() {
        // On<ConnectorCreated>((state, evt) => state with {
        // 	ConnectorId   = evt.ConnectorId,
        // 	Name          = evt.Name,
        // 	ConnectorType = evt.ConnectorType,
        // 	Subscription  = evt.Subscription,
        // 	Settings      = evt.Settings,
        // 	TenantId      = evt.TenantId
        // });
        //
        // On<ConnectorUpdated>((state, evt) => state with {
        // 	Revisions = state.Revisions
        // });
        //
        // On<ConnectorDeleted>((state, evt) => state with {
        // 	// System = state.System.With(x => x.IsDeleted = true)
        // });
        //
        // On<ConnectorEnabled>((state, evt) => state with {
        // 	RunningState = ConnectorState.Enabled
        // });
        //
        // On<ConnectorDisabled>((state, evt) => state with {
        // 	RunningState = ConnectorState.Disabled
        // });
        //
        // On<ConnectorStateChanged>((state, evt) => state with {
        // 	RunningState = evt.CurrentState
        // });
        //
        // On<ConnectorRenamed>((state, evt) => state with {
        // 	Name = evt.Name
        // });
    }

    /// Unique identifier of the connector
    public string ConnectorId { get; init; } = Guid.Empty.ToString();

    /// Name of the connector
    public string Name { get; init; } = "";

    /// Custom settings for the connector
    public Dictionary<int, ConnectorRevision> Revisions { get; init; } = [];

    public ConnectorRevision CurrentRevision { get; init; } = new();

    public ConnectorState State { get; init; } = ConnectorState.Unknown;
    
    public List<RecordPosition> Positions { get; init; } = new();
    
    public SystemData System { get; init; } = new();
}

public static class ConnectorDomainServices {
    public delegate Task<(Dictionary<string, string> InvalidSettings, bool Valid)> ValidateConnectorSettings(string connectorInstanceType,
        Dictionary<string, string> settings, CancellationToken cancellationToken = default);
}

public static class ConnectorDomainExceptions {
    public class ConnectorDeleted(string connectorId)
        : DomainException($"Connector {connectorId} is deleted") {
        public string ConnectorId { get; } = connectorId;

        public static void Throw(ConnectorEntity connector) =>
            throw new ConnectorDeleted(connector.ConnectorId);
    }

    public class ConnectorInvalidState(string connectorId, ConnectorState currentState, ConnectorState requestedState)
        : DomainException($"Connector {connectorId} invalid state change from {currentState} to {requestedState} detected") {
        public string         ConnectorId    { get; } = connectorId;
        public ConnectorState CurrentState   { get; } = currentState;
        public ConnectorState RequestedState { get; } = requestedState;

        public static void Throw(string connectorId, ConnectorState currentState, ConnectorState requestedState) =>
            throw new ConnectorInvalidState(connectorId, currentState, requestedState);
    }

    public class InvalidConnectorSettings(string connectorId, Dictionary<string, string> invalidSettings)
        : DomainException($"Connector {connectorId} invalid settings detected") {
        public string                     ConnectorId     { get; } = connectorId;
        public Dictionary<string, string> InvalidSettings { get; } = invalidSettings;

        public static void Throw(string connectorId, Dictionary<string, string> invalidSettings) =>
            throw new InvalidConnectorSettings(connectorId, invalidSettings);
    }
}