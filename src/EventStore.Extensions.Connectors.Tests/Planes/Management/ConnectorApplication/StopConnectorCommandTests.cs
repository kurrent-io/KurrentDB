using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Toolkit.Testing.Fixtures;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

[Trait("Category", "Management")]
public class StopConnectorCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task stop_connector_when_connector_exists_and_running() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, StopConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
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
                new StopConnector {
                    ConnectorId = connectorId
                }
            )
            .Then(
                new ConnectorDeactivating {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task no_event_when_connector_stopped() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, StopConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
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
                new StopConnector {
                    ConnectorId = connectorId
                }
            )
            .Then();
    }

    [Fact]
    public async Task no_event_when_connector_deactivating() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, StopConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                },
                new ConnectorDeactivating {
                    ConnectorId = connectorId,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new StopConnector {
                    ConnectorId = connectorId
                }
            )
            .Then();
    }

    // [Fact]
    // public async Task no_event_when_connector_activating() {
    //     var connectorId   = Fixture.NewConnectorId();
    //     var connectorName = Fixture.NewConnectorName();
    //
    //     await CommandServiceSpec<ConnectorEntity, StopConnector>.Builder
    //         .ForService(Fixture.ConnectorApplication)
    //         .Given(
    //             new ConnectorCreated {
    //                 ConnectorId = connectorId,
    //                 Name        = connectorName,
    //                 Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
    //             },
    //             new ConnectorActivating {
    //                 ConnectorId = connectorId,
    //                 Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
    //             }
    //         )
    //         .When(
    //             new StopConnector {
    //                 ConnectorId = connectorId
    //             }
    //         )
    //         .Then();
    // }

    [Fact]
    public async Task should_throw_domain_exception_when_stopping_deleted_connector() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();

        await CommandServiceSpec<ConnectorEntity, StopConnector>.Builder
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
                new StopConnector {
                    ConnectorId = connectorId
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeletedException(connectorId));
    }
}