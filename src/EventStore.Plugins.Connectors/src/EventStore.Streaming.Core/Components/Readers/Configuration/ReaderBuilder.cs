using EventStore.Streaming.Schema;
using Polly;

namespace EventStore.Streaming.Readers.Configuration;

public interface IReaderBuilder<out TBuilder> {
	TBuilder BufferSize(int messagePrefetchCount);
	
	TBuilder SchemaRegistry(Func<SchemaRegistry> getSchemaRegistry);
	TBuilder SkipDecoding(bool skipDecoding = true);
	TBuilder LoggerFactory(ILoggerFactory loggerFactory);
	TBuilder EnableLogging(bool enableLogging = true);

	TBuilder ResiliencePipeline(ResiliencePipelineBuilder builder);
	TBuilder ResiliencePipeline(Action<ResiliencePipelineBuilder> build);
	TBuilder ResiliencePipeline(string name, Action<ResiliencePipelineBuilder> build);
	
	// IReader Create();
}

[PublicAPI]
public abstract record ReaderBuilder<TBuilder, TOptions, TReader> : IReaderBuilder<TBuilder>
	where TBuilder : ReaderBuilder<TBuilder, TOptions, TReader>
	where TOptions : ReaderOptions<TOptions>, new()
	where TReader : IReader {
	protected ReaderBuilder(TOptions? options = null) => Options = options ?? new TOptions();

	protected internal TOptions Options { get; init; }

	public TBuilder ReaderName(string readerName) {
		return (TBuilder)this with {
			Options = Options with {
				ReaderName = readerName
			}
		};
	}
    
	/// <summary>
	/// The maximum number of messages to prefetch from the server.
	/// <para />
	/// [ Default: 100 | Importance: high ]
	/// </summary>
	public TBuilder BufferSize(int messagePrefetchCount) {
		Ensure.Positive(messagePrefetchCount);
		return (TBuilder)this with {
			Options = Options with {
				BufferSize = messagePrefetchCount
			}
		};
	}

	public TBuilder SchemaRegistry(Func<SchemaRegistry> getSchemaRegistry) {
		Ensure.NotNull(getSchemaRegistry);

		return (TBuilder)this with {
			Options = Options with {
				GetSchemaRegistry = getSchemaRegistry
			}
		};
	}
	
	public TBuilder SkipDecoding(bool skipDecoding = true) {
		return (TBuilder)this with {
			Options = Options with {
				SkipDecoding = skipDecoding
			}
		};	
	}

	public TBuilder LoggerFactory(ILoggerFactory loggerFactory) {
		Ensure.NotNull(loggerFactory);
		return (TBuilder)this with {
			Options = Options with {
				LoggerFactory = loggerFactory
			}
		};
	}
	
	public TBuilder EnableLogging(bool enableLogging = true) {
		return (TBuilder)this with {
			Options = Options with {
				EnableLogging = enableLogging
			}
		};	
	}
	
	public TBuilder ResiliencePipeline(ResiliencePipelineBuilder builder) {
		Ensure.NotNull(builder);
		return (TBuilder)this with {
			Options = Options with {
				ResiliencePipelineBuilder = builder 
			}
		};
	}
	public TBuilder ResiliencePipeline(Action<ResiliencePipelineBuilder> build) {
		var builder = new ResiliencePipelineBuilder();
		build(builder);
		return ResiliencePipeline(builder);
	}
	
	public TBuilder ResiliencePipeline(string name, Action<ResiliencePipelineBuilder> build) => 
		ResiliencePipeline(x  => x.Name = name);

	public abstract TReader Create();
}