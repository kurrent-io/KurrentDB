using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;
using Google.Protobuf.WellKnownTypes;

using static EventStore.Connectors.Management.ConnectorDomainServices;
using static Eventuous.FuncServiceDelegates;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorsApplication : CommandService<ConnectorEntity> {
    static GetStreamNameFromCommand<dynamic> FromCommand() => cmd =>
        new(ConnectorsFeatureConventions.Streams.GetManagementStream(cmd.ConnectorId));

    public ConnectorsApplication(ValidateConnectorSettings validateSettings, IEventStore store, TimeProvider time) : base(store) {
        On<CreateConnector>()
            .InState(ExpectedState.New)
            .GetStream(FromCommand())
            .Act((_, _, cmd) => {
                var settings = ConnectorSettings
                    .From(cmd.Settings, validateSettings)
                    .EnsureValid(cmd.ConnectorId)
                    .ToDictionary();

                return [
                    new ConnectorCreated {
                        ConnectorId = cmd.ConnectorId,
                        Name        = cmd.Name,
                        Settings    = { settings },
                        Timestamp   = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        On<ReconfigureConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act((connector, _, cmd) => {
                connector.EnsureNotDeleted();
                connector.EnsureStopped();

                var settings = ConnectorSettings
                    .From(cmd.Settings, validateSettings)
                    .EnsureValid(cmd.ConnectorId)
                    .ToDictionary();

                return [
                    new ConnectorReconfigured {
                        ConnectorId = connector.Id,
                        Revision    = connector.CurrentRevision.Number + 1,
                        Settings    = { settings },
                        Timestamp   = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        On<DeleteConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act((connector, _, _) => {
                connector.EnsureNotDeleted();
                connector.EnsureStopped();

                return [
                    new ConnectorDeleted {
                        ConnectorId = connector.Id,
                        Timestamp   = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        On<StartConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act((connector, _, _) => {
                connector.EnsureNotDeleted();

                // if (connector.State
                //     is ConnectorState.Running
                //     or ConnectorState.Activating
                //     or ConnectorState.Deactivating)
                //     return [];

                if (connector.State
                    is ConnectorState.Running
                    or ConnectorState.Activating)
                    throw new DomainException($"Connector {connector.Id} already running...");

                connector.EnsureStopped();

                return [
                    new ConnectorActivating {
                        ConnectorId = connector.Id,
                        Settings    = { connector.CurrentRevision.Settings },
                        Timestamp   = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        On<StopConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act((connector, _, _) => {
                connector.EnsureNotDeleted();

                if (connector.State
                    is ConnectorState.Stopped
                    or ConnectorState.Deactivating)
                    return [];

                connector.EnsureRunning();

                return [
                    new ConnectorDeactivating {
                        ConnectorId = connector.Id,
                        Timestamp   = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        On<ResetConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act((connector, _, cmd) => {
                connector.EnsureNotDeleted();
                connector.EnsureStopped();

                return [
                    new ConnectorReset {
                        ConnectorId = connector.Id,
                        LogPosition = cmd.LogPosition,
                        Timestamp   = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });

        On<RenameConnector>()
            .InState(ExpectedState.Existing)
            .GetStream(FromCommand())
            .Act(
                (connector, _, cmd) => {
                    connector.EnsureNotDeleted();

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
                    connector.EnsureNotDeleted();

                    // need to do all the state change validations here:
                    // Stopped -> Activating (implicit)

                    // Activating -> Running
                    // Running -> Deactivating
                    // Deactivating -> Stopped

                    // Activating -> Stopped * Faulted?
                    // Running -> Stopped * Faulted?
                    // Deactivating -> Stopped * Faulted?

                    // ** Activating -> Failed (Stopped with error details)
                    // ** Running -> Failed (Stopped with error details)
                    // ** Deactivating -> Failed (Stopped with error details)

                    var now = time.GetUtcNow().ToTimestamp();

                    return cmd switch {
                        { ToState: ConnectorState.Running } => [
                            new ConnectorRunning {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp,
                                RecordedAt  = now
                            }
                        ],
                        { ToState: ConnectorState.Stopped, ErrorDetails: null } => [
                            new ConnectorStopped {
                                ConnectorId = connector.Id,
                                Timestamp   = cmd.Timestamp,
                                RecordedAt  = now
                            }
                        ],
                        { ToState: ConnectorState.Stopped, ErrorDetails: not null } => [
                            new ConnectorFailed {
                                ConnectorId  = connector.Id,
                                ErrorDetails = cmd.ErrorDetails,
                                Timestamp    = cmd.Timestamp,
                                RecordedAt   = now
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
                connector.EnsureNotDeleted();
                connector.EnsureNewLogPositionIsValid(cmd.LogPosition);

                return [
                    new ConnectorPositionCommitted {
                        ConnectorId = connector.Id,
                        LogPosition = cmd.LogPosition,
                        Timestamp   = cmd.Timestamp,
                        RecordedAt  = time.GetUtcNow().ToTimestamp()
                    }
                ];
            });
    }
}