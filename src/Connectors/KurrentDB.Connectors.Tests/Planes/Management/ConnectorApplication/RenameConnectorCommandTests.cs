using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Toolkit.Testing.Fixtures;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

[Trait("Category", "Management")]
public class RenameConnectorCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task rename_connector_when_connector_exists() {
        var connectorId      = Fixture.NewConnectorId();
        var connectorName    = Fixture.NewConnectorName();
        var newConnectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RenameConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RenameConnector {
                    ConnectorId = connectorId,
                    Name        = newConnectorName
                }
            )
            .Then(
                new ConnectorRenamed {
                    ConnectorId = connectorId,
                    Name        = newConnectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task connector_deleted_exception_when_renaming_deleted_connector() {
        var connectorId      = Fixture.NewConnectorId();
        var connectorName    = Fixture.NewConnectorName();
        var newConnectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RenameConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
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
                new RenameConnector {
                    ConnectorId = connectorId,
                    Name        = newConnectorName
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeletedException(connectorId));
    }

    [Fact]
    public async Task connector_renamed_with_same_name() {
        var connectorId      = Fixture.NewConnectorId();
        var connectorName    = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RenameConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RenameConnector {
                    ConnectorId = connectorId,
                    Name        = connectorName
                }
            )
            .Then();
    }
}