using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Toolkit.Testing.Fixtures;
using Eventuous;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

[Trait("Category", "Management")]
public class RecordConnectorPositionCommandTests(ITestOutputHelper output, CommandServiceFixture fixture) : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task records_position_successfully() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var logPosition   = Fixture.Faker.Random.ULong();

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPosition>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorPosition {
                    ConnectorId = connectorId,
                    LogPosition = logPosition,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(
                new ConnectorPositionCommitted {
                    ConnectorId = connectorId,
                    LogPosition = logPosition,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp(),
                    RecordedAt  = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task should_throw_domain_exception_when_connector_is_deleted() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var logPosition   = Fixture.Faker.Random.ULong();

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPosition>.Builder
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
                new RecordConnectorPosition {
                    ConnectorId = connectorId,
                    LogPosition = logPosition,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeletedException(connectorId));
    }

    [Fact]
    public async Task should_throw_domain_exception_when_positions_are_older_than_last_committed() {
        var connectorId    = Fixture.NewConnectorId();
        var connectorName  = Fixture.NewConnectorName();
        var oldLogPosition = Fixture.Faker.Random.ULong(1);
        var newLogPosition = oldLogPosition - 1;

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPosition>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorPositionCommitted {
                    ConnectorId = connectorId,
                    LogPosition = oldLogPosition,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp(),
                    RecordedAt  = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorPosition {
                    ConnectorId = connectorId,
                    LogPosition = newLogPosition,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(new DomainException("New positions cannot be older than the last committed positions."));
    }
}