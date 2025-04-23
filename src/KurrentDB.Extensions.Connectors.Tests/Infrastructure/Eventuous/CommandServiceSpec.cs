using EventStore.Connectors;
using Eventuous;
using Eventuous.Testing;
using Serilog;
using static Serilog.Core.Constants;

namespace EventStore.Extensions.Connectors.Tests.Eventuous;

public class CommandServiceSpec<TState, TCommand> where TState : State<TState>, new() where TCommand : class, new() {
    static readonly ILogger Logger = Log.ForContext(
        SourceContextPropertyName, $"CommandServiceSpec<{typeof(TState).Name}, {typeof(TCommand).Name}>"
    );

    Queue<object>    GivenEvents         { get; set; } = [];
    TCommand         WhenCommand         { get; set; } = null!;
    Queue<object>    ThenEvents          { get; set; } = [];
    DomainException? ThenDomainException { get; set; }

    IEventStore             EventStore { get; set; } = new SystemInMemoryEventStore();
    ICommandService<TState> Service    { get; set; } = null!;

    public static CommandServiceSpec<TState, TCommand> Builder => new CommandServiceSpec<TState, TCommand>();

    public CommandServiceSpec<TState, TCommand> ForService(Func<IEventStore, ICommandService<TState>> serviceFactory) {
        Service = serviceFactory(EventStore);
        return this;
    }

    public CommandServiceSpec<TState, TCommand> WithEventStore(IEventStore eventStore) {
        EventStore = eventStore;
        return this;
    }

    public CommandServiceSpec<TState, TCommand> GivenNoState() => this;

    public CommandServiceSpec<TState, TCommand> Given(params object[] domainEvents) {
        foreach (var evt in domainEvents) {
            GivenEvents.Enqueue(evt);
            RegisterEventsInTypeMap(evt);
        }

        return this;
    }

    public CommandServiceSpec<TState, TCommand> When(TCommand command) {
        WhenCommand = command;
        return this;
    }

    public async Task Then(params object[] domainEvents) {
        foreach (var evt in domainEvents) {
            ThenEvents.Enqueue(evt);
            RegisterEventsInTypeMap(evt);
        }

        await Assert();
    }


    public async Task Then(DomainException domainException) {
        ThenDomainException = domainException;
        await Assert();
    }

    async Task Assert() {
        // TODO SS: super hacky way to get the stream name. must fix soon. perhaps we should not be using the eventstore here...
        var streamName = ConnectorsFeatureConventions.Streams.ManagementStreamTemplate.GetStream(((dynamic)WhenCommand).ConnectorId);

        // Given the following events.
        if (GivenEvents.Count != 0)
            Logger.Information("Given events {GivenEvents}", GivenEvents.Select(x => x.GetType().Name));
        else
            Logger.Information("Given no state");

        await EventStore.AppendEvents(
            new StreamName(streamName),
            ExpectedStreamVersion.NoStream,
            GivenEvents.Select(evt => new StreamEvent(Guid.NewGuid(), evt, new Metadata(), "application/json", 0)).ToList(),
            CancellationToken.None
        );

        // When I send the following command to my service.
        Logger.Information("When {CommandType} {Command}", WhenCommand.GetType().Name, WhenCommand);

        var commandResult = await Service.Handle(WhenCommand, CancellationToken.None);

        // Then I expect the following events to be raised.

        if (ThenDomainException is null) {
            if (commandResult.Changes?.Any() ?? false)
                Logger.Information("Then events raised must be {ThenEvents}", ThenEvents.Select(x => x.GetType().Name));
            else
                Logger.Information("Then no events are raised");

            var actualEvents = commandResult.Changes?.Select(c => c.Event);
            actualEvents.Should().BeEquivalentTo(ThenEvents);
            return;
        }

        Logger.Error(ThenDomainException, "Then {ThenDomainExceptionType} is thrown", ThenDomainException.GetType().Name);

        // OR I expect a domain exception to be thrown.
        commandResult.Should().BeOfType<ErrorResult<TState>>();
        commandResult.As<ErrorResult<TState>>().Exception.Should().BeOfType(ThenDomainException.GetType());
    }

    static void RegisterEventsInTypeMap(object domainEvent) =>
        TypeMap.Instance.AddType(domainEvent.GetType(), domainEvent.GetType().FullName!);
}