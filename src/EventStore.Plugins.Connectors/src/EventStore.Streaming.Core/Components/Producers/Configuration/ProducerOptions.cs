using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Resilience;
using EventStore.Streaming.Schema;
using Polly;

namespace EventStore.Streaming.Producers.Configuration;

[PublicAPI]
public abstract record ProducerOptions {
	/// <summary>
	/// Set the producer name.
	/// <para />
	/// [ Optional ]
	/// </summary>
	public string ProducerName { get; init; } = "";

	/// <summary>
	/// Set the default stream to produce messages to.
	/// <para />
	/// [ Optional | Importance: medium ]
	/// </summary>
	public string? DefaultStream { get; init; }
	
	/// <summary>
	/// The factory used to retrieve a schema registry.
	/// </summary>
	public SchemaRegistry SchemaRegistry { get; init; } = SchemaRegistry.Global;
	
	/// <summary>
	/// The logger factory used to create a logger for the consumer.
	/// Used to configure the logging system and create instances of <see cref="T:Microsoft.Extensions.Logging.ILogger" /> from
	/// the registered <see cref="T:Microsoft.Extensions.Logging.ILoggerProvider" />s.
	/// </summary>
	public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;
	
	/// <summary>
	/// Whether to enable logging.
	/// </summary>
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