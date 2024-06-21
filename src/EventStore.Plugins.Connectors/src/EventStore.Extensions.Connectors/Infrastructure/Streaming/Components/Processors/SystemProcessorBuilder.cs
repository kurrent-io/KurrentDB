// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Producers;

namespace EventStore.Streaming.Processors.Configuration;

[PublicAPI]
public record SystemProcessorBuilder : ProcessorBuilder<SystemProcessorBuilder, SystemProcessorOptions> {
	public SystemProcessorBuilder Filter(ConsumeFilter filter) =>
        new() {
            Options = Options with {
                Filter = filter
            }
        };

    public SystemProcessorBuilder Streams(params string[] streams) =>
        Filter(ConsumeFilter.Streams(streams));

    public SystemProcessorBuilder InitialPosition(SubscriptionInitialPosition subscriptionInitialPosition) =>
        new() {
            Options = Options with {
                InitialPosition = subscriptionInitialPosition
            }
        };

    public SystemProcessorBuilder StartPosition(RecordPosition recordPosition) =>
        new() {
            Options = Options with {
                StartPosition = recordPosition,
                LogPosition   = recordPosition.LogPosition
            }
        };

    public SystemProcessorBuilder LogPosition(LogPosition logPosition) =>
        new() {
            Options = Options with {
                LogPosition   = logPosition,
                StartPosition = RecordPosition.FromLogPosition(logPosition)
            }
        };

    public SystemProcessorBuilder AutoCommit(Func<AutoCommitOptions, AutoCommitOptions> configureAutoCommit) =>
        new() {
            Options = Options with {
                AutoCommit = configureAutoCommit(Options.AutoCommit)
            }
        };

    public SystemProcessorBuilder AutoCommit(int interval, int recordsThreshold) =>
        new() {
            Options = Options with {
                AutoCommit = Options.AutoCommit with {
                    Interval = TimeSpan.FromMilliseconds(interval),
                    RecordsThreshold = recordsThreshold
                }
            }
        };

    public SystemProcessorBuilder DisableAutoCommit() =>
        AutoCommit(x => x with { AutoCommitEnabled = false });

    public SystemProcessorBuilder SkipDecoding(bool skipDecoding = true) =>
        new() {
            Options = Options with {
                SkipDecoding = skipDecoding
            }
        };

    public SystemProcessorBuilder Publisher(IPublisher publisher) {
		Ensure.NotNull(publisher);
		return new() {
			Options = Options with {
				Publisher = publisher
			}
		};
	}

	public override SystemProcessor Create() {
		Ensure.NotNullOrWhiteSpace(Options.ProcessorId);
		Ensure.NotNullOrWhiteSpace(Options.SubscriptionName);
        Ensure.NotNullOrEmpty(Options.RouterRegistry.Endpoints);
		Ensure.NotNull(Options.Publisher);

		var options = Options with { };

        options = Options with {
            GetConsumer = () => SystemConsumer.Builder
                .Publisher(options.Publisher)
                .ConsumerId($"{options.ProcessorId}-csr")
                .SubscriptionName(options.SubscriptionName)
                .Filter(options.Filter)
                .InitialPosition(options.InitialPosition)
                .StartPosition(options.StartPosition)
                .LogPosition(options.LogPosition)
                .AutoCommit(options.AutoCommit)
                .SkipDecoding(options.SkipDecoding)
                .LoggerFactory(options.LoggerFactory)
                .EnableLogging(options.EnableLogging)
                .SchemaRegistry(options.SchemaRegistry)
                .Create(),

            GetProducer = () => SystemProducer.Builder
                .Publisher(options.Publisher)
                .ProducerId($"{options.ProcessorId}-pdr")
                .LoggerFactory(options.LoggerFactory)
                .EnableLogging(options.EnableLogging)
                .SchemaRegistry(options.SchemaRegistry)
                .Create()
        };

        return new SystemProcessor(options);
	}
}