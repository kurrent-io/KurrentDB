using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Testing.Fixtures;
using Eventuous;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

public class RecordConnectorPositionsConnectorStateChangeCommandTests(
    ITestOutputHelper output,
    CommandServiceFixture fixture
) : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task record_positions_successfully() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var positions     = new List<ulong> { 1, 2, 3 };

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPositions>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorPositions {
                    ConnectorId = connectorId,
                    Positions   = { positions },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(
                new ConnectorPositionsCommitted {
                    ConnectorId = connectorId,
                    Positions   = { positions },
                    CommittedAt = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task should_throw_domain_exception_when_connector_is_deleted() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var positions     = new List<ulong> { 1, 2, 3 };

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPositions>.Builder
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
                new RecordConnectorPositions {
                    ConnectorId = connectorId,
                    Positions   = { positions },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeleted(connectorId));
    }

    [Fact]
    public async Task should_throw_domain_exception_when_positions_are_null_or_empty() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPositions>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorPositions {
                    ConnectorId = connectorId,
                    Positions   = { },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(new DomainException("Positions list cannot be null or empty."));
    }

    [Fact]
    public async Task should_throw_domain_exception_when_positions_are_older_than_last_committed() {
        var connectorId      = Fixture.NewConnectorId();
        var connectorName    = Fixture.NewConnectorName();
        var initialPositions = new List<ulong> { 3, 4, 5 };
        var newPositions     = new List<ulong> { 1, 2 };

        await CommandServiceSpec<ConnectorEntity, RecordConnectorPositions>.Builder
            .WithService(Fixture.CreateConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorPositionsCommitted {
                    ConnectorId = connectorId,
                    Positions   = { initialPositions },
                    CommittedAt = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new RecordConnectorPositions {
                    ConnectorId = connectorId,
                    Positions   = { newPositions },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .Then(new DomainException("New positions cannot be older than the last committed positions."));
    }
}