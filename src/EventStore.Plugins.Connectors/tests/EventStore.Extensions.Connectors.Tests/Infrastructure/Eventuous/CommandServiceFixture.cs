using EventStore.Testing.Fixtures;
using Eventuous;
using ConnectorsManagement = EventStore.Connectors.Management;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace EventStore.Extensions.Connectors.Tests.Eventuous;

// ReSharper disable once MemberCanBePrivate.Global
[UsedImplicitly]
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