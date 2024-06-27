using Eventuous;
using EventStore.Testing.Fixtures;
using ConnectorsManagement = EventStore.Connectors.Management;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace EventStore.Extensions.Connectors.Tests.CommandService;

// ReSharper disable once MemberCanBePrivate.Global
public class CommandServiceFixture : FastFixture {
    public ConnectorsManagement.ConnectorApplication CreateConnectorApplication(IEventStore eventStore)
        => CreateConnectorApplication(
            eventStore,
            validationResult: new ValidationResult()
        );

    public ConnectorsManagement.ConnectorApplication CreateConnectorApplication(
        IEventStore eventStore,
        ValidationResult validationResult
    ) =>
        new ConnectorsManagement.ConnectorApplication(
            _ => validationResult,
            eventStore,
            TimeProvider
        );
}