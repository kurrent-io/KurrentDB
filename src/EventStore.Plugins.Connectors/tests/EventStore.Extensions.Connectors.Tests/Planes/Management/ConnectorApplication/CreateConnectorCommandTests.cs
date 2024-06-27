using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Extensions.Connectors.Tests.CommandService;
using EventStore.Testing.Fixtures;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace EventStore.Extensions.Connectors.Tests.Management.ConnectorApplication;

public class CreateConnectorCommandTests(ITestOutputHelper output, CommandServiceFixture fixture)
    : FastTests<CommandServiceFixture>(output, fixture) {
    [Fact]
    public async Task ShouldCreateConnectorWhenConnectorDoesNotAlreadyExist() {
        var connectorId   = Fixture.NewConnectorId();
        var connectorName = Fixture.NewConnectorName();
        var settings      = new Dictionary<string, string> { { "Setting1Key", "Setting1Value" } };

        await CommandServiceSpec<ConnectorEntity, CreateConnector>.Builder
            .WithService(Fixture.CreateConnectorApplication)
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
    public async Task ShouldThrowDomainExceptionWhenConnectorSettingsAreInvalid() {
        var connectorId = Fixture.NewConnectorId();
        var forcedValidationResult =
            new ValidationResult(new[] { new ValidationFailure("SomeProperty", "Validation failure!") });

        await CommandServiceSpec<ConnectorEntity, CreateConnector>.Builder
            .WithService(
                eventStore => Fixture.CreateConnectorApplication(
                    eventStore,
                    forcedValidationResult
                )
            )
            .GivenNoState()
            .When(
                new CreateConnector {
                    ConnectorId = connectorId,
                    Name        = Fixture.NewConnectorName(),
                    Settings    = { new Dictionary<string, string>() }
                }
            )
            .Then(
                new ConnectorDomainExceptions.InvalidConnectorSettings(
                    connectorId,
                    new() { { "SomeProperty", ["Validation failure!"] } }
                )
            );
    }
}