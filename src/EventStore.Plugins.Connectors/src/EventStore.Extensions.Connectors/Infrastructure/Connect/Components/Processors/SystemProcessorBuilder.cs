// ReSharper disable CheckNamespace

using System.Diagnostics.CodeAnalysis;
using EventStore.Connect.Consumers;
using EventStore.Connect.Leases;
using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Processors.Locks;
using Microsoft.Extensions.Logging;
using NodaTime.Extensions;
using Polly;

namespace EventStore.Connect.Processors.Configuration;

[PublicAPI]
public record SystemProcessorBuilder : ProcessorBuilder<SystemProcessorBuilder, SystemProcessorOptions> {
	public SystemProcessorBuilder Filter(ConsumeFilter filter) =>
        new() {
            Options = Options with {
                Filter = filter
            }
        };

    public SystemProcessorBuilder Stream(StreamId stream) =>
        Filter(ConsumeFilter.Stream(stream));

    public SystemProcessorBuilder StartPosition(RecordPosition recordPosition, bool reset = false) =>
        new() {
            Options = Options with {
                StartPosition = recordPosition,
                ResetPosition = reset
            }
        };

    public SystemProcessorBuilder AutoCommit(AutoCommitOptions autoCommitOptions) =>
        new() {
            Options = Options with {
                AutoCommit = autoCommitOptions
            }
        };

    public SystemProcessorBuilder AutoCommit(Func<AutoCommitOptions, AutoCommitOptions> configureAutoCommit) =>
        new() {
            Options = Options with {
                AutoCommit = configureAutoCommit(Options.AutoCommit)
            }
        };

    public SystemProcessorBuilder DisableAutoCommit() =>
        AutoCommit(x => x with { Enabled = false });

    public SystemProcessorBuilder SkipDecoding(bool skipDecoding = true) =>
        new() {
            Options = Options with {
                SkipDecoding = skipDecoding
            }
        };

    public SystemProcessorBuilder DisableAutoLock() =>
        AutoLock(x => x with { Enabled = false });

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

        var loggingOptions = new LoggingOptions {
            Enabled = options.Logging.Enabled,
            LoggerFactory = options.Logging.LoggerFactory
        };

        var leaseManager = new LeaseManager(
            SystemReader.Builder
                .Publisher(options.Publisher)
                .ReaderId($"rdx-leases-{options.ProcessorId}")
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions with { LogName = "LeaseManagerSystemReader" })
                .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),
            SystemProducer.Builder
                .Publisher(options.Publisher)
                .ProducerId($"pdx-leases-{options.ProcessorId}")
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions with { LogName = "LeaseManagerSystemProducer" })
                .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),
            streamTemplate: options.AutoLock.StreamTemplate,
            logger: options.Logging.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var serviceLockerOptions = new ServiceLockerOptions {
            ResourceId    = options.ProcessorId,
            OwnerId       = options.AutoLock.OwnerId,
            LeaseDuration = options.AutoLock.LeaseDuration.ToDuration(),
            Retry = new() {
                Timeout = options.AutoLock.AcquisitionTimeout.ToDuration(),
                Delay   = options.AutoLock.AcquisitionDelay.ToDuration()
            }
        };

        options = Options with {
            GetConsumer = () => SystemConsumer.Builder
                .Publisher(options.Publisher)
                .ConsumerId(options.ProcessorId)
                .SubscriptionName(options.SubscriptionName)
                .Filter(options.Filter)
                .StartPosition(options.StartPosition, options.ResetPosition)
                .AutoCommit(options.AutoCommit)
                .SkipDecoding(options.SkipDecoding)
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions)
                .Transformer(options.Transformer)
                .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),

            GetProducer = () => SystemProducer.Builder
                .Publisher(options.Publisher)
                .ProducerId(options.ProcessorId)
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions)
                .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),

            GetLocker = () => new ServiceLocker(serviceLockerOptions, leaseManager)
        };

        return new SystemProcessor(options);
	}
}