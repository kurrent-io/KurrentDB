// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Leases;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Locks;
using KurrentDB.Core;
using KurrentDB.Surge.Consumers;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using Microsoft.Extensions.Logging;
using NodaTime.Extensions;

namespace KurrentDB.Surge.Processors;

[PublicAPI]
public record SystemProcessorBuilder : ProcessorBuilder<SystemProcessorBuilder, SystemProcessorOptions> {
    public SystemProcessorBuilder Client(ISystemClient client) {
		Ensure.NotNull(client);
		return new() {
			Options = Options with {
				Client = client
			}
		};
	}

    [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
	public override IProcessor Create() {
		Ensure.NotNullOrWhiteSpace(Options.ProcessorId);
		Ensure.NotNullOrWhiteSpace(Options.SubscriptionName);
        Ensure.NotNullOrEmpty(Options.RouterRegistry.Endpoints);
		Ensure.NotNull(Options.Client);

		var options = Options with { };

        var loggingOptions = new LoggingOptions {
            Enabled = options.Logging.Enabled,
            LoggerFactory = options.Logging.LoggerFactory
        };

        var leaseManager = new LeaseManager(
            SystemReader.Builder
                .Client(options.Client)
                .ReaderId($"leases-{options.ProcessorId}")
                .SchemaRegistry(options.SchemaRegistry)
                .Logging(loggingOptions with { Enabled = false, LogName = "LeaseManagerSystemReader" })
                // .ResiliencePipeline(new ResiliencePipelineBuilder().AddPipeline(ResiliencePipeline.Empty))
                .Create(),
            SystemProducer.Builder
                .Client(options.Client)
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
                .Client(options.Client)
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
                .Client(options.Client)
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
