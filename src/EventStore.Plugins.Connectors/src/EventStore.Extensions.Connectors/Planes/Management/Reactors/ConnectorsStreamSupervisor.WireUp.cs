using EventStore.Connect.Processors.Configuration;
using EventStore.Connectors.System;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.ConnectorsFeatureConventions.Streams;

namespace EventStore.Connectors.Management.Reactors;

static class ConnectorsStreamSupervisorWireUp {
    public static IServiceCollection AddConnectorsStreamSupervisor(this IServiceCollection services) {
        return services.AddSingleton<IHostedService, ConnectorsStreamSupervisorService>(ctx => {
            const string serviceName = "ConnectorsStreamSupervisor";

            return new ConnectorsStreamSupervisorService(() => {
                var publisher           = ctx.GetRequiredService<IPublisher>();
                var loggerFactory       = ctx.GetRequiredService<ILoggerFactory>();
                var getProcessorBuilder = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();

                var options = new ConnectorsStreamSupervisorOptions {
                    Leases      = new(MaxCount: 10),
                    Checkpoints = new(MaxCount: 10),
                    Lifetime    = new(MaxCount: 10)
                };

                var processor = getProcessorBuilder()
                    .ProcessorId(serviceName)
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = loggerFactory,
                        LogName       = "EventStore.Connect.Processors.SystemProcessor"
                        // LogName       = "EventStore.Connectors.Management.ConnectorsStreamSupervisor"
                    })
                    .DisableAutoLock()
                    .AutoCommit(new AutoCommitOptions {
                        Enabled          = true,
                        RecordsThreshold = 100,
                        StreamTemplate   = ManagementStreamSupervisorCheckpointsStream
                    })
                    .PublishStateChanges(new PublishStateChangesOptions { Enabled = false })
                    .InitialPosition(SubscriptionInitialPosition.Earliest)
                    .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
                    .WithModule(new ConnectorsStreamSupervisor(publisher, options))
                    .Create();

                return processor;
            }, ctx, serviceName);
        });
    }
}

class ConnectorsStreamSupervisorService(Func<IProcessor> getProcessor, IServiceProvider serviceProvider, string serviceName)
    : LeaderNodeProcessorWorker<IProcessor>(getProcessor, serviceProvider, serviceName);