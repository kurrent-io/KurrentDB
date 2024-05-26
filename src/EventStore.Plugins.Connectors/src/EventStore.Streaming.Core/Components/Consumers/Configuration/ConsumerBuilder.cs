using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Schema;
using Polly;
using static System.String;

namespace EventStore.Streaming.Consumers.Configuration;

[PublicAPI]
public abstract record ConsumerBuilder<TBuilder, TOptions, TConsumer>
	where TBuilder : ConsumerBuilder<TBuilder, TOptions, TConsumer>
	where TOptions : ConsumerOptions, new()
	where TConsumer : IConsumer {
	protected ConsumerBuilder(TOptions? options = null) => Options = options ?? new TOptions();

	protected internal TOptions Options { get; init; }

	public TBuilder SubscriptionName(string subscriptionName) {
		return IsNullOrWhiteSpace(subscriptionName)
			? (TBuilder)this
			: (TBuilder)this with {
				Options = Options with {
					SubscriptionName = subscriptionName,
					ConsumerName = IsNullOrWhiteSpace(Options.ConsumerName) ? subscriptionName : Options.ConsumerName
				}
			};
	}

	public TBuilder ConsumerName(string consumerName) {
		return IsNullOrWhiteSpace(consumerName)
			? (TBuilder)this
			: (TBuilder)this with {
				Options = Options with {
					ConsumerName = consumerName,
					SubscriptionName = IsNullOrWhiteSpace(Options.SubscriptionName) ? consumerName : Options.SubscriptionName,
					// ClientSettings   = Options.ClientSettings.With(x => x.ConnectionName = consumerName)
				}
			};
	}

	public TBuilder Streams(params string[] streams) {
		return (TBuilder)this with {
			Options = Options with {
				Streams = streams
			}
		};
	}

	public TBuilder Filter(ConsumeFilter filter) {
		return (TBuilder)this with {
			Options = Options with {
				Filter = filter
			}
		};
	}

	public TBuilder InitialPosition(SubscriptionInitialPosition subscriptionInitialPosition) {
		return (TBuilder)this with {
			Options = Options with {
				InitialPosition = subscriptionInitialPosition
			}
		};
	}
	
	public TBuilder StartPosition(RecordPosition recordPosition) {
		return (TBuilder)this with {
			Options = Options with {
				StartPosition = recordPosition
			}
		};
	}

	public TBuilder MessagePrefetchCount(int messagePrefetchCount) {
		Ensure.Positive(messagePrefetchCount);
		return (TBuilder)this with {
			Options = Options with {
				MessagePrefetchCount = messagePrefetchCount
			}
		};
	}

	public TBuilder AutoCommit(Func<AutoCommitOptions, AutoCommitOptions> configureAutoCommit) {
		Ensure.NotNull(configureAutoCommit);

		return (TBuilder)this with {
			Options = Options with {
				AutoCommit = configureAutoCommit(Options.AutoCommit)
			}
		};
	}
	
	public TBuilder AutoCommit(AutoCommitOptions autoCommitOptions) {
		Ensure.NotNull(autoCommitOptions);

		return (TBuilder)this with {
			Options = Options with {
				AutoCommit = autoCommitOptions
			}
		};
	}
	
	public TBuilder DisableAutoCommit() => 
		AutoCommit(x => x with { AutoCommitEnabled = false });

	public TBuilder SchemaRegistry(SchemaRegistry getSchemaRegistry) {
		Ensure.NotNull(getSchemaRegistry);

		return (TBuilder)this with {
			Options = Options with {
				SchemaRegistry = getSchemaRegistry
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
		Ensure.NotNullOrEmpty(interceptors);

		return (TBuilder)this with {
			Options = Options with {
				Interceptors = interceptors
			}
		};
	}
	
	public abstract TConsumer Create();
}