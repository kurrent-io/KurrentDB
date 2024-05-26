using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Routing;
using EventStore.Streaming.Schema;

namespace EventStore.Streaming.Processors.Configuration;

[PublicAPI]
public abstract record ProcessorOptions<TOptions> where TOptions : ProcessorOptions<TOptions> {
    protected ProcessorOptions() {
        var id = Identifiers.GenerateShortId(prefix: "prx");

        ProcessorId      = $"{id}-1";
        ProcessorName    = id;
        SubscriptionName = id;
    }

    public string                      ProcessorId          { get; init; } = "";
    public string                      ProcessorName        { get; init; } = "";
	public string                      SubscriptionName     { get; init; } = "";
	public string[]                    Streams              { get; init; } = [];
	public ConsumeFilter               Filter               { get; init; } = ConsumeFilter.ExcludeSystemEvents();
	public SubscriptionInitialPosition InitialPosition      { get; init; } = SubscriptionInitialPosition.Latest;
	public RecordPosition              StartPosition        { get; init; } = RecordPosition.Unset;
	public int                         MessagePrefetchCount { get; init; } = 1000;
	public AutoCommitOptions           AutoCommit           { get; init; } = new();
	public string?                     DefaultOutputStream  { get; init; }
	public IStateStore                 StateStore           { get; init; } = new InMemoryStateStore();
	public SchemaRegistry              SchemaRegistry       { get; init; } = SchemaRegistry.Global;
	public bool                        SkipDecoding         { get; init; }
	
	public LinkedList<InterceptorModule> Interceptors         { get; init; } = [];
	public ILoggerFactory                LoggerFactory        { get; init; } = NullLoggerFactory.Instance;
	public bool                          EnableLogging        { get; init; }

    public MessageRouterRegistry RouterRegistry { get; init; } = new();

	public Func<TOptions, IConsumer> GetConsumer { get; init; } = null!;
	public Func<TOptions, IProducer> GetProducer { get; init; } = null!;

    public TOptions Duplicate()           => (TOptions)(this with { });
    public TOptions MemberwiseDuplicate() => (TOptions)MemberwiseClone();
}