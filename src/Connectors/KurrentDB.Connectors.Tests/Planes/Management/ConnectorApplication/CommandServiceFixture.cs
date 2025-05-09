using EventStore.Toolkit.Testing.Fixtures;
using Eventuous;
using ConnectorsManagement = EventStore.Connectors.Management;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace EventStore.Extensions.Connectors.Tests.Eventuous;

[UsedImplicitly]
public class CommandServiceFixture : FastFixture {
    public ConnectorsManagement.ConnectorsApplication ConnectorApplication(IEventStore eventStore) =>
        ConnectorApplication(eventStore, validationResult: new ValidationResult());

    public ConnectorsManagement.ConnectorsApplication ConnectorApplication(IEventStore eventStore, ValidationResult validationResult) =>
        new ConnectorsManagement.ConnectorsApplication(_ => validationResult, eventStore, TimeProvider);
}