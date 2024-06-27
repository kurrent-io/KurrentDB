using DotNext.Collections.Generic;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using Eventuous;

namespace EventStore.Extensions.Connectors.Tests.CommandService;

public class CommandServiceSpec<TState, TCommand>
    where TState : State<TState>, new()
    where TCommand : class, new() {
    dynamic[]        GivenEvents         { get; set; } = [];
    TCommand         WhenCommand         { get; set; } = null!;
    dynamic[]        ThenEvents          { get; set; } = [];
    DomainException? ThenDomainException { get; set; }

    InMemoryEventStore               EventStore { get; set; } = null!;
    FunctionalCommandService<TState> Service    { get; set; } = null!;

    public static CommandServiceSpec<TState, TCommand> Builder => new CommandServiceSpec<TState, TCommand>();

    public CommandServiceSpec<TState, TCommand> WithService(
        Func<IEventStore, FunctionalCommandService<TState>> serviceFactory
    ) {
        EventStore = new InMemoryEventStore();
        Service    = serviceFactory(EventStore);
        return this;
    }

    public CommandServiceSpec<TState, TCommand> Given(params object[] events) {
        GivenEvents = events;
        RegisterEventsInTypeMap(events);
        return this;
    }

    public CommandServiceSpec<TState, TCommand> When(TCommand command) {
        WhenCommand = command!;
        return this;
    }

    public async Task Then(params object[] events) {
        ThenEvents = events;
        RegisterEventsInTypeMap(events);
        await Assert();
    }

    public async Task Then(DomainException domainException) {
        ThenDomainException = domainException;
        await Assert();
    }

    async Task Assert() {
        var streamName = $"$connector-{((dynamic)WhenCommand).ConnectorId}";

        // Given the following events.
        await EventStore.AppendEvents(
            new StreamName(streamName),
            ExpectedStreamVersion.NoStream,
            GivenEvents.Select(e => new StreamEvent(Guid.NewGuid(), e, new Metadata(), "application/json", 0)).ToList(),
            CancellationToken.None
        );

        // When I send the following command to my service.
        var commandResult = await Service.Handle(WhenCommand, CancellationToken.None);

        // Then I expect a domain exception to be thrown.
        if (ThenDomainException is not null) {
            commandResult.Should().BeOfType<ErrorResult<TState>>();
            commandResult.As<ErrorResult<TState>>().Exception.Should().BeOfType(ThenDomainException.GetType());
            return;
        }

        // OR I expect the following events to be emitted.
        var actualEvents = commandResult.Changes?.Select(c => c.Event);
        actualEvents.Should().BeEquivalentTo(ThenEvents);
    }

    static void RegisterEventsInTypeMap(params object[] events) =>
        events.ForEach(evt => TypeMap.Instance.AddType(evt.GetType(), evt.GetType().Name));
}