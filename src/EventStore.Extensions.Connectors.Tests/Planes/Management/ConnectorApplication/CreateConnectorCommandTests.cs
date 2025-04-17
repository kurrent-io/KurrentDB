using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Toolkit.Testing.Fixtures;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

[Trait("Category", "Management")]
public class CreateConnectorCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task should_create_connector_when_connector_does_not_already_exist() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var settings      = new Dictionary<string, string> { { "Setting1Key", "Setting1Value" } };

        await CommandServiceSpec<ConnectorEntity, CreateConnector>.Builder
            .ForService(Fixture.ConnectorApplication)
            .GivenNoState()
            .When(
                new CreateConnector {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Settings    = { settings }
                }
            )
            .Then(
                new ConnectorCreated {
                    ConnectorId = connectorId,
                    Name        = connectorName,
                    Settings    = { settings },
                    Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
                }
            );
    }

    [Fact]
    public async Task should_throw_domain_exception_when_connector_settings_are_invalid() {
        var connectorId = Fixture.NewConnectorId();
        var forcedValidationResult = new ValidationResult([new ValidationFailure("SomeProperty", "Validation failure!")]);

        await CommandServiceSpec<ConnectorEntity, CreateConnector>.Builder
            .ForService(eventStore => Fixture.ConnectorApplication(eventStore, forcedValidationResult))
            .GivenNoState()
            .When(
                new CreateConnector {
                    ConnectorId = connectorId,
                    Name        = Fixture.NewConnectorName(),
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