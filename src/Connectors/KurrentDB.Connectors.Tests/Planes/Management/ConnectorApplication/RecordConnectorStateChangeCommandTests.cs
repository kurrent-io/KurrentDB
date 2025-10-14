using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Toolkit.Testing.Fixtures;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

[Trait("Category", "Management")]
public class RecordConnectorStateChangeCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task record_state_change_to_running() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RecordConnectorStateChange>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorStateChange {
                    ConnectorId = connectorId,
                    ToState     = ConnectorState.Running,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(
                new ConnectorRunning {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp(),
                    RecordedAt = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task record_state_change_to_stopped() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RecordConnectorStateChange>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorStateChange {
                    ConnectorId  = connectorId,
                    ToState      = ConnectorState.Stopped,
                    ErrorDetails = null,
                    Timestamp    = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(
                new ConnectorStopped {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp(),
                    RecordedAt  = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task should_throw_domain_exception_when_connector_is_deleted() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RecordConnectorStateChange>.Builder
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
                new RecordConnectorStateChange {
                    ConnectorId = connectorId,
                    ToState     = ConnectorState.Running,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp(),
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeletedException(connectorId));
    }
}