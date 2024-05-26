using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Resilience;
using EventStore.Streaming.Schema;
using Polly;

namespace EventStore.Streaming.Consumers.Configuration;

[PublicAPI]
public abstract record ConsumerOptions {
	/// <summary>
	/// Set the consumer name.
	/// [ Optional ]
	/// </summary>
	public string ConsumerName { get; init; } = "";

	/// <summary>
	/// Set the subscription name for this consumer.
	/// <para />
	/// [ Optional ]
	/// </summary>
	public string SubscriptionName { get; init; } = "";

	/// <summary>
	/// Set the streams to read from.
	/// <para />
	/// [ Required ]
	/// </summary>
	public string[] Streams { get; init; } = [];

	/// <summary>
	/// Set the search Filter
	/// <para />
	/// [ Required ]
	/// </summary>
	public ConsumeFilter Filter { get; init; } = ConsumeFilter.None;

	/// <summary>
	/// Initial position at which the cursor will be set when subscribing and no start position is set.
	/// <para />
	/// [ Default: Latest | Importance: high ]
	/// </summary>
	public SubscriptionInitialPosition InitialPosition { get; init; } = SubscriptionInitialPosition.Latest;
	
	/// <summary>
	/// The position at which the cursor will be set when subscribing.
	/// <para />
	/// [ Optional ]
	/// </summary>
	public RecordPosition StartPosition { get; init; } = RecordPosition.Unset;
	
	// /// <summary>
	// /// The position at which the cursor will be set when subscribing.
	// /// <para />
	// /// [ Optional ]
	// /// </summary>
	// public LogPosition StartPosition { get; init; } = LogPosition.Unset;

	/// <summary>
	/// The number of messages to prefetch from the server.
	/// <para />
	/// [ Default: 1000 | Importance: medium ]
	/// </summary>
	public int MessagePrefetchCount { get; init; } = 1000;
	
	public AutoCommitOptions AutoCommit { get; init; } = new();
	
	// /// <summary>
	// /// The factory used to retrieve a schema for the consumer.
	// /// </summary>
	// public Func<ISchema> GetSchema { get; init; } = () => new JsonSchema();
	
	/// <summary>
	/// The factory used to retrieve a schema registry.
	/// </summary>
	public SchemaRegistry SchemaRegistry { get; init; } = SchemaRegistry.Global;

	/// <summary>
	/// Skips the decoding of the message data.
	/// This is useful when the consumer is only interested in handling raw data.
	/// </summary>
	public bool SkipDecoding { get; init; }

	/// <summary>
	/// The logger factory used to create a logger for the consumer.
	/// Used to configure the logging system and create instances of <see cref="T:Microsoft.Extensions.Logging.ILogger" /> from
	/// the registered <see cref="T:Microsoft.Extensions.Logging.ILoggerProvider" />s.
	/// </summary>
	public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;
	
	public bool EnableLogging { get; init; }
	
	/// <summary>
	/// The resilience pipeline builder used to handle transient faults.
	/// </summary>
	public ResiliencePipelineBuilder ResiliencePipelineBuilder { get; init; } = RetryPolicies.ConstantBackoffPipeline();

	/// <summary>
	/// The interceptors used to intercept the consumer lifetime events.
	/// </summary>
	public LinkedList<InterceptorModule> Interceptors { get; init; } = [];
}