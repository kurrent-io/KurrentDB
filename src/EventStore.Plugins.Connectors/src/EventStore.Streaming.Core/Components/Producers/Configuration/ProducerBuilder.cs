using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Schema;
using Polly;
using static System.String;

namespace EventStore.Streaming.Producers.Configuration;

[PublicAPI]
public abstract record ProducerBuilder<TBuilder, TOptions, TProducer>
	where TBuilder : ProducerBuilder<TBuilder, TOptions, TProducer>
	where TOptions : ProducerOptions, new()
	where TProducer : IProducer {
	protected ProducerBuilder(TOptions? options = null) => Options = options ?? new TOptions();

	protected internal TOptions Options { get; init; }

	public TBuilder ProducerName(string producerName) {
		return IsNullOrWhiteSpace(producerName)
			? (TBuilder)this
			: (TBuilder)this with {
				Options = Options with {
					ProducerName = producerName
				}
			};
	}

	public TBuilder DefaultStream(string? defaultStream) {
		return (TBuilder)this with {
			Options = Options with {
				DefaultStream = defaultStream
			}
		};
	}
	
	public TBuilder SchemaRegistry(SchemaRegistry getSchemaRegistry) {
		Ensure.NotNull(getSchemaRegistry);

		return (TBuilder)this with {
			Options = Options with {
				SchemaRegistry = getSchemaRegistry
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

	public TBuilder InterceptFirst(InterceptorModule interceptor) {
		Ensure.NotNull(interceptor);

		return (TBuilder)this with {
			Options = Options with {
				Interceptors = Options.Interceptors.With(x => x.AddUniqueFirst(interceptor))
			}
		};
	}

	public TBuilder InterceptLast(InterceptorModule interceptor) {
		Ensure.NotNull(interceptor);

		return (TBuilder)this with {
			Options = Options with {
				Interceptors = Options.Interceptors.With(x => x.AddUniqueLast(interceptor))
			}
		};
	}

	public TBuilder Interceptors(LinkedList<InterceptorModule> interceptors) {
		Ensure.NotNull(interceptors);

		return (TBuilder)this with {
			Options = Options with {
				Interceptors = interceptors
			}
		};
	}

	public abstract TProducer Create();
}

