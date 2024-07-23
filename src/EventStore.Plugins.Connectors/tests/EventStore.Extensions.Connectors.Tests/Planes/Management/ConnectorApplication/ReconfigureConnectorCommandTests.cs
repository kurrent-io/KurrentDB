using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Toolkit.Testing.Fixtures;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using ValidationResult = FluentValidation.Results.ValidationResult;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

[Trait("Category", "Management")]
public class ReconfigureConnectorCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task reconfigure_connector_when_exists_and_not_deleted() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var settings      = new MapField<string, string> { { "key", "value" } };

        await CommandServiceSpec<ConnectorEntity, ReconfigureConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new ReconfigureConnector {
                    ConnectorId = connectorId,
                    Settings    = { settings }
                }
            )
            .Then(
                new ConnectorReconfigured {
                    ConnectorId = connectorId,
                    Revision    = 1,
                    Settings    = { settings },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task should_throw_domain_exception_when_connector_deleted() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var settings      = new MapField<string, string> { { "key", "value" } };

        await CommandServiceSpec<ConnectorEntity, ReconfigureConnector>.Builder
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
                new ReconfigureConnector {
                    ConnectorId = connectorId,
                    Settings    = { settings }
                }
            )
            .Then(new ConnectorDomainExceptions.ConnectorDeletedException(connectorId));
    }

    [Fact]
    public async Task should_throw_domain_exception_when_settings_invalid() {
        var connectorId            = Fixture.NewConnectorId();
        var connectorName          = Fixture.NewConnectorName();
        var forcedValidationResult = new ValidationResult([new ValidationFailure("SomeProperty", "Validation failure!")]);

        await CommandServiceSpec<ConnectorEntity, ReconfigureConnector>.Builder
            .ForService(
                eventStore => Fixture.ConnectorApplication(
                    eventStore,
                    forcedValidationResult
                )
            )
            .Given(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            )
            .When(
                new ReconfigureConnector {
                    ConnectorId = connectorId,
                    Settings    = { new Dictionary<string, string>() }
                }
            )
            .Then(
                new ConnectorDomainExceptions.InvalidConnectorSettingsException(
                    connectorId,
                    new Dictionary<string, string[]> { { "SomeProperty", ["Validation failure!"] } }
                )
            );
    }
}