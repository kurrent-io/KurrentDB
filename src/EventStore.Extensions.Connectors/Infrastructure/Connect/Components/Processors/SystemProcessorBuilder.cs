// ReSharper disable CheckNamespace

using System.Diagnostics.CodeAnalysis;
using EventStore.Connect.Consumers;
using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Leases;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Locks;
using Kurrent.Toolkit;
using Microsoft.Extensions.Logging;
using NodaTime.Extensions;

namespace EventStore.Connect.Processors.Configuration;

[PublicAPI]
public record SystemProcessorBuilder : ProcessorBuilder<SystemProcessorBuilder, SystemProcessorOptions> {
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
                .ReaderId($"leases-{options.ProcessorId}")
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions with { Enabled = false, LogName = "LeaseManagerSystemReader" })
                // .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),
            SystemProducer.Builder
                .Publisher(options.Publisher)
                .ProducerId($"leases-{options.ProcessorId}")
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions with { Enabled = false, LogName = "LeaseManagerSystemProducer" })
                // .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
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
                .StartPosition(options.StartPosition)
                .InitialPosition(options.InitialPosition)
                .AutoCommit(options.AutoCommit)
                .SkipDecoding(options.SkipDecoding)
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions)
                // .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),

            GetProducer = () => SystemProducer.Builder
                .Publisher(options.Publisher)
                .ProducerId(options.ProcessorId)
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions)
                // .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),

            GetLocker = () => new ServiceLocker(serviceLockerOptions, leaseManager)
        };

        return new SystemProcessor(options);
	}
}