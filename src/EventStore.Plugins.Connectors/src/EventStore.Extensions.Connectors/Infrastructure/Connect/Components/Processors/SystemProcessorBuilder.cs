// ReSharper disable CheckNamespace

using System.Diagnostics.CodeAnalysis;
using EventStore.Connect.Consumers;
using EventStore.Connect.Leases;
using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Processors.Locks;
using Microsoft.Extensions.Logging;
using NodaTime.Extensions;

namespace EventStore.Connect.Processors.Configuration;

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

    [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
	public override IProcessor Create() {
		Ensure.NotNullOrWhiteSpace(Options.ProcessorId);
		Ensure.NotNullOrWhiteSpace(Options.SubscriptionName);
        Ensure.NotNullOrEmpty(Options.RouterRegistry.Endpoints);
		Ensure.NotNull(Options.Publisher);

		var options = Options with { };

        var reader = SystemReader.Builder
            .Publisher(options.Publisher)
            .ReaderId($"{options.ProcessorId}-leases-rdr")
            //.LoggerFactory(options.LoggerFactory)
            //.EnableLogging(options.EnableLogging)
            .SchemaRegistry(options.SchemaRegistry)
            .Create();

        var producer = SystemProducer.Builder
            .Publisher(options.Publisher)
            .ProducerId($"{options.ProcessorId}-leases-pdr")
            //.LoggerFactory(options.LoggerFactory)
            //.EnableLogging(options.EnableLogging)
            .SchemaRegistry(options.SchemaRegistry)
            .Create();

        var leaseManager = new LeaseManager(
            reader, producer, streamPrefix: options.AutoLock.StreamNamespace,
            logger: options.LoggerFactory.CreateLogger<LeaseManager>()
        );

        options = Options with {
            GetConsumer = () => SystemConsumer.Builder
                .Publisher(options.Publisher)
                .ConsumerId(options.ProcessorId)
                .SubscriptionName(options.SubscriptionName)
                .Filter(options.Filter)
                .InitialPosition(options.InitialPosition)
                .StartPosition(options.StartPosition)
                .AutoCommit(options.AutoCommit)
                .SkipDecoding(options.SkipDecoding)
                .LoggerFactory(options.LoggerFactory)
                .EnableLogging(options.EnableLogging)
                .SchemaRegistry(options.SchemaRegistry)
                .Create(),

            GetProducer = () => SystemProducer.Builder
                .Publisher(options.Publisher)
                .ProducerId(options.ProcessorId)
                .LoggerFactory(options.LoggerFactory)
                .EnableLogging(options.EnableLogging)
                .SchemaRegistry(options.SchemaRegistry)
                .Create(),

            GetLocker = () => new ServiceLocker(
                new ServiceLockerOptions {
                    ResourceId    = options.ProcessorId,
                    OwnerId       = options.AutoLock.OwnerId,
                    LeaseDuration = options.AutoLock.LeaseDuration.ToDuration(),
                    Retry = new() {
                        Timeout = options.AutoLock.AcquisitionTimeout.ToDuration(),
                        Delay   = options.AutoLock.AcquisitionDelay.ToDuration()
                    }
                },
                leaseManager
            )
        };

        return new SystemProcessor(options);
	}
}