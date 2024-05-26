using EventStore.Streaming.Resilience;
using EventStore.Streaming.Schema;
using Polly;

namespace EventStore.Streaming.Readers.Configuration;

[PublicAPI]
public abstract record ReaderOptions<TOptions> where TOptions : ReaderOptions<TOptions> {
	/// <summary>
	/// Set the reader name.
	/// <para />
	/// [ Optional ]
	/// </summary>
	public string ReaderName { get; init; } = "default";
    
	/// <summary>
	/// The maximum number of messages to prefetch from the server.
	/// <para />
	/// [ Default: 100 | Importance: high ]
	/// </summary>
	public int BufferSize { get; init; } = 100;
    
	/// <summary>
	/// The factory used to retrieve a schema registry.
	/// </summary>
	public Func<SchemaRegistry> GetSchemaRegistry { get; init; } = () => SchemaRegistry.Global;

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
	
	/// <summary>
	/// Whether to enable logging.
	/// </summary>
	public bool EnableLogging { get; init; }
	
	/// <summary>
	/// The resilience pipeline builder used to handle transient faults.
	/// </summary>
	public ResiliencePipelineBuilder ResiliencePipelineBuilder { get; init; } = RetryPolicies.ConstantBackoffPipeline();
}