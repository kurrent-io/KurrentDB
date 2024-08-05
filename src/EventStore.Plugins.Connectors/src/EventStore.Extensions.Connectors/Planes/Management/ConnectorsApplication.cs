using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using Eventuous;
using Google.Protobuf.WellKnownTypes;
using static EventStore.Connectors.Management.ConnectorDomainServices;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorsApplication : EntityApplication<ConnectorEntity> {
    public ConnectorsApplication(ValidateConnectorSettings validateSettings, TimeProvider time, IEventStore store) : base(cmd => cmd.ConnectorId, ConnectorsFeatureConventions.Streams.ManagementStreamTemplate, store) {
        OnNew<CreateConnector>(cmd => {
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

        OnExisting<DeleteConnector>((connector, cmd) => {
            connector.EnsureNotDeleted();
            connector.EnsureStopped();

            return [
                new ConnectorDeleted {
                    ConnectorId = connector.Id,
                    Timestamp   = time.GetUtcNow().ToTimestamp()
                }
            ];
        });

        OnExisting<ReconfigureConnector>((connector, cmd) => {
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

        OnExisting<StartConnector>((connector, cmd) => {
            connector.EnsureNotDeleted();

            if (connector.State
                is ConnectorState.Running
                or ConnectorState.Activating)
                throw new DomainException($"Connector {connector.Id} already running...");

            connector.EnsureStopped();

            return [
                new ConnectorActivating {
                    ConnectorId   = connector.Id,
                    Settings      = { connector.CurrentRevision.Settings },
                    StartPosition = cmd.StartPosition,
                    Timestamp     = time.GetUtcNow().ToTimestamp()
                }
            ];
        });

        OnExisting<StopConnector>((connector, _) => {
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

        OnExisting<RenameConnector>((connector, cmd) => {
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
        });

        OnExisting<RecordConnectorStateChange>((connector, cmd) => {
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
        });
    }
}