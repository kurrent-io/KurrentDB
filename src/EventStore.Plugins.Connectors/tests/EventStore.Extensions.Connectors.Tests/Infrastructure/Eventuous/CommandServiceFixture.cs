using EventStore.Testing.Fixtures;
using Eventuous;
using ConnectorsManagement = EventStore.Connectors.Management;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace EventStore.Extensions.Connectors.Tests.Eventuous;

[UsedImplicitly]
public class CommandServiceFixture : FastFixture {
    public ConnectorsManagement.ConnectorApplication ConnectorApplication(IEventStore eventStore) =>
        ConnectorApplication(eventStore, validationResult: new ValidationResult());

    public ConnectorsManagement.ConnectorApplication ConnectorApplication(IEventStore eventStore, ValidationResult validationResult) =>
        new ConnectorsManagement.ConnectorApplication(_ => validationResult, eventStore, TimeProvider);
}