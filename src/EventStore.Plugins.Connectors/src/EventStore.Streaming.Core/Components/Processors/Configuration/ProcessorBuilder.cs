using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Routing;
using EventStore.Streaming.Schema;
using static System.String;

namespace EventStore.Streaming.Processors.Configuration;

[PublicAPI]
public abstract record ProcessorBuilder<TBuilder, TOptions>
	where TBuilder : ProcessorBuilder<TBuilder, TOptions>, new() 
	where TOptions : ProcessorOptions<TOptions>, new() {
	protected ProcessorBuilder(TOptions? options = null) => Options = options ?? new TOptions();

	protected internal TOptions Options { get; init; }

    public TBuilder OverrideProcessorId(string processorId) {
        Ensure.NotNullOrWhiteSpace(processorId);
        
        return (TBuilder)this with {
                Options = Options with {
                    ProcessorId = processorId
                }
            };
    }

    public TBuilder ProcessorName(string processorName) =>
		IsNullOrWhiteSpace(processorName)
			? (TBuilder)this
			: (TBuilder)this with {
				Options = Options with {
					ProcessorName    = processorName,
					SubscriptionName = IsNullOrWhiteSpace(Options.SubscriptionName) ? processorName : Options.SubscriptionName
				}
			};

	public TBuilder SubscriptionName(string subscriptionName) =>
		IsNullOrWhiteSpace(subscriptionName)
			? (TBuilder)this
			: (TBuilder)this with {
				Options = Options with {
					SubscriptionName = subscriptionName,
					ProcessorName    = IsNullOrWhiteSpace(Options.ProcessorName) ? subscriptionName : Options.ProcessorName
				}
			};

	public TBuilder LoggerFactory(ILoggerFactory loggerFactory) {
		Ensure.NotNull(loggerFactory);

		return (TBuilder)this with {
			Options = Options with {
				LoggerFactory = loggerFactory
			}
		};
	}

	public TBuilder Streams(params string[] streams) =>
		(TBuilder)this with {
			Options = Options with {
				Streams = streams
			}
		};

	public TBuilder Filter(ConsumeFilter filter) =>
		(TBuilder)this with {
			Options = Options with {
				Filter = filter
			}
		};

	public TBuilder InitialPosition(SubscriptionInitialPosition subscriptionInitialPosition) =>
		(TBuilder)this with {
			Options = Options with {
				InitialPosition = subscriptionInitialPosition
			}
		};
	
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
	
	public TBuilder DefaultOutputStream(string stream) {
		Ensure.NotNull(stream);

		return (TBuilder)this with {
			Options = Options with {
				DefaultOutputStream = stream
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

	public TBuilder AutoCommit(int interval, int recordsThreshold) {
		return (TBuilder)this with {
			Options = Options with {
				AutoCommit = Options.AutoCommit with {
					Interval = TimeSpan.FromMilliseconds(interval),
					RecordsThreshold = recordsThreshold
				}
			}
		};
	}
	
	public TBuilder DisableAutoCommit() => 
		AutoCommit(x => x with { AutoCommitEnabled = false });
	
	public TBuilder SchemaRegistry(SchemaRegistry schemaRegistry) {
		Ensure.NotNull(schemaRegistry);
		return (TBuilder)this with {
			Options = Options with {
				SchemaRegistry = schemaRegistry
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

	public TBuilder StateStore(IStateStore stateStore) {
		Ensure.NotNull(stateStore);
		return (TBuilder)this with {
			Options = Options with {
				StateStore = stateStore
			}
		};
	}

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
    
    public TBuilder WithHandler(IRecordHandler handler) {
        Ensure.NotNull(handler);
        return (TBuilder)this with {
            Options = Options.Duplicate().With(x => x.RouterRegistry.RegisterHandler(handler))
        };
    }
    
    public TBuilder WithHandler(Route route, IRecordHandler handler) {
        Ensure.NotNull(handler);
        return (TBuilder)this with {
            Options = Options.Duplicate().With(x => x.RouterRegistry.RegisterHandler(route, handler))
        };
    }
	
    public TBuilder WithHandlers(IEnumerable<IRecordHandler> handlers) {
        return (TBuilder)this with {
            Options = Options.Duplicate().With(x => {
                foreach (var handler in handlers)
                    x.RouterRegistry.RegisterHandler(handler);
            })
        };
    }
	
    public TBuilder WithModule(ProcessingModule module) {
        return (TBuilder)this with {
            Options = Options.Duplicate().With(x => x.RouterRegistry.With(module.RegisterHandlersOn))
        };
    }
	
    public TBuilder WithModules(IEnumerable<ProcessingModule> modules) {
        return (TBuilder)this with {
            Options = Options.Duplicate().With(x => {
                foreach (var module in modules)
                    x.RouterRegistry.With(module.RegisterHandlersOn);
            })
        };
    }
    
    public TBuilder Process(ProcessRecord handler) {
        Ensure.NotNull(handler);
        return WithHandler(new RecordHandler.Proxy(handler));
    }

    public TBuilder Process<T>(Route route, ProcessRecord<T> handler) {
        Ensure.NotNull(handler);
        return WithHandler(route, new RecordHandler<T>.Proxy(handler));
    }
    
	public TBuilder Process<T>(ProcessRecord<T> handler) {
		Ensure.NotNull(handler);
		return WithHandler(new RecordHandler<T>.Proxy(handler));
	}
	
    public TBuilder Process(ProcessRecordSynchronously handler) {
        Ensure.NotNull(handler);
        return WithHandler(new SynchronousRecordHandler.Proxy(handler));
    }
    
    	
    public TBuilder Process<T>(Route route, ProcessRecordSynchronously<T> handler) {
        Ensure.NotNull(handler);
        return WithHandler(route, new SynchronousRecordHandler<T>.Proxy(handler));
    }
    
    public TBuilder Process<T>(ProcessRecordSynchronously<T> handler) {
        Ensure.NotNull(handler);
        return WithHandler(new SynchronousRecordHandler<T>.Proxy(handler));
    }
	
	public TBuilder EnableLogging(bool enableLogging = true) {
		return (TBuilder)this with {
			Options = Options with {
				EnableLogging = enableLogging
			}
		};	
	}
	
	public TBuilder OutputStream(string defaultOutputStream) {
		return (TBuilder)this with {
			Options = Options with {
				DefaultOutputStream = defaultOutputStream
			}
		};
	}

	public abstract IProcessor Create();
}