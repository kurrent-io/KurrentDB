using EventStore.Connect.Processors.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.System;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.Queries.ConnectorQueryConventions;

namespace EventStore.Connectors.Management.Projectors;

static class ConnectorsStateProjectorWireUp {
    public static IServiceCollection AddConnectorsStateProjector(this IServiceCollection services) {
        return services
            .AddSingleton(new ConnectorsStateProjectorOptions {
                Filter           = Filters.ConnectorsQueryStateFilter,
                SnapshotStreamId = Streams.ConnectorsQueryProjectionStream
            })
            .AddSingleton<IHostedService, ConnectorsStateProjectorService>(ctx => {
                var publisher           = ctx.GetRequiredService<IPublisher>();
                var loggerFactory       = ctx.GetRequiredService<ILoggerFactory>();
                var options             = ctx.GetRequiredService<ConnectorsStateProjectorOptions>();
                var getProcessorBuilder = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();
                var getReaderBuilder    = ctx.GetRequiredService<Func<SystemReaderBuilder>>();

                var processor = getProcessorBuilder()
                    .ProcessorId("connectors-state-projector-px")
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = loggerFactory,
                        LogName       = "ConnectorsStateProjector"
                    })
                    .DisableAutoLock()
                    .AutoCommit(new AutoCommitOptions {
                        Enabled          = true,
                        RecordsThreshold = 1
                    })
                    .Filter(options.Filter)
                    .InitialPosition(SubscriptionInitialPosition.Earliest)
                    .WithModule(new ConnectorsStateProjector(options,
                        publisher,
                        getReaderBuilder))
                    .Create();

                return new ConnectorsStateProjectorService(processor, ctx);
            });
    }
}

class ConnectorsStateProjectorService(IProcessor processor, IServiceProvider serviceProvider)
    : LeadershipAwareProcessorWorker<IProcessor>(processor, serviceProvider);