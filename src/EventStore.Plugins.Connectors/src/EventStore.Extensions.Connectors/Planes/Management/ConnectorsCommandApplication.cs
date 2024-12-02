using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Toolkit;
using Eventuous;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.ConnectorDomainServices;

namespace EventStore.Connectors.Management;

[PublicAPI]
public class ConnectorsCommandApplication : EntityApplication<ConnectorEntity> {
    public ConnectorsCommandApplication(ValidateConnectorSettings validateSettings, ConnectorsLicenseService licenseService, TimeProvider time, IEventStore store) :
        base(cmd => cmd.ConnectorId, ConnectorsFeatureConventions.Streams.ManagementStreamTemplate, store) {
        OnAny<CreateConnector>((connector, cmd) => {
            connector.EnsureIsNew();

            var settings = ConnectorSettings
                .From(cmd.Settings)
                .EnsureValid(cmd.ConnectorId, validateSettings)
                .AsDictionary();

            CheckAccess(settings, licenseService);

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
            CheckAccess(connector, licenseService);

            connector.EnsureNotDeleted();

            // until the connector is restarted, it wont use the new settings

            var settings = ConnectorSettings
                .From(cmd.Settings)
                .EnsureValid(cmd.ConnectorId, validateSettings)
                .AsDictionary();

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
            CheckAccess(connector, licenseService);

            connector.EnsureNotDeleted();

            if (connector.State
                is ConnectorState.Running
                or ConnectorState.Activating)
                throw new DomainException($"Connector {connector.Id} already running...");

            connector.EnsureStopped();

            return [
                new ConnectorActivating {
                    ConnectorId = connector.Id,
                    Settings    = { connector.CurrentRevision.Settings },
                    StartFrom   = cmd.StartFrom,
                    Timestamp   = time.GetUtcNow().ToTimestamp()
                }
            ];
        });

        OnExisting<ResetConnector>((connector, cmd) => {
            CheckAccess(connector, licenseService);

            connector.EnsureNotDeleted();

            if (connector.State
                is ConnectorState.Running
                or ConnectorState.Activating)
                throw new DomainException($"Connector {connector.Id} already running...");

            connector.EnsureStopped();

            return [
                new ConnectorActivating {
                    ConnectorId = connector.Id,
                    Settings    = { connector.CurrentRevision.Settings },
                    StartFrom   = cmd.StartFrom ?? new StartFromPosition(), // reset to beginning, this is the big difference from StartConnector
                    Timestamp   = time.GetUtcNow().ToTimestamp()
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
            CheckAccess(connector, licenseService);

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

            // To make it idempotent, we ignore all messages that are older than the current state
            if (cmd.Timestamp <= connector.StateTimestamp)
                return [];

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

    static void CheckAccess(IDictionary<string, string?> settings, ConnectorsLicenseService licenseService) {
        var options = new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetRequiredOptions<SinkOptions>();
        if (!licenseService.CheckLicense(options.InstanceTypeName, out var info))
            throw new ConnectorAccessDeniedException($"Usage of the {info.ConnectorType.Name} connector is not autorized");
    }

    static void CheckAccess(ConnectorEntity connector, ConnectorsLicenseService licenseService) {
        var instanceType = connector.CurrentRevision.Settings
            .First(kvp => kvp.Key.Equals(nameof(SinkOptions.InstanceTypeName), StringComparison.OrdinalIgnoreCase)).Value;

        if (!licenseService.CheckLicense(instanceType, out var info))
            throw new ConnectorAccessDeniedException($"Usage of the {info.ConnectorType.Name} connector is not autorized");
    }
}