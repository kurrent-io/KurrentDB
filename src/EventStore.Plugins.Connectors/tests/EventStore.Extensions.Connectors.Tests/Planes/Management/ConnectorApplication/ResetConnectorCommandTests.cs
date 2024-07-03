using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Testing.Fixtures;
using Eventuous;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

public class ResetConnectorCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task reset_connector_when_connector_exists_and_stopped() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var positions     = new List<ulong> { 1, 2, 3 };

        await CommandServiceSpec<ConnectorEntity, ResetConnector>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorStopped {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new ResetConnector {
                    ConnectorId = connectorId,
                    Positions   = { positions }
                }
            )
            .Then(
                new ConnectorReset {
                    ConnectorId = connectorId,
                    Positions   = { positions },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task domain_exception_when_resetting_deleted_connector() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, ResetConnector>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorDeleted {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new ResetConnector {
                    ConnectorId = connectorId,
                    Positions   = { 1, 2, 3 }
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeleted(connectorId));
    }

    [Fact]
    public async Task domain_exception_when_resetting_running_connector() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, ResetConnector>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorRunning {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new ResetConnector {
                    ConnectorId = connectorId,
                    Positions   = { 1, 2, 3 }
                }
            )
            .Then(
                new DomainException($"Connector {connectorId} must be running. Current state: {ConnectorState.Running}")
            );
    }

    [Fact]
    public async Task reset_connector_without_positions() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, ResetConnector>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorStopped {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new ResetConnector {
                    ConnectorId = connectorId,
                    Positions = {
                        Capacity = 0
                    }
                }
            )
            .Then(
                new ConnectorDomainExceptions.ConnectorInvalidState(
                    connectorId,
                    ConnectorState.Stopped,
                    ConnectorState.Stopped
                )
            );
    }
}