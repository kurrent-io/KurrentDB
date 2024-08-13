using EventStore.Connect.Processors.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Management.Queries;
using EventStore.Connectors.System;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Data;

static class ConnectorsStateProjectorWireUp {
    public static IServiceCollection AddConnectorsStateProjection(this IServiceCollection services) {
        return services
           .AddSingleton<IHostedService, ConnectorsStateProjectionService>(ctx => {
                var loggerFactory       = ctx.GetRequiredService<ILoggerFactory>();
                var getProcessorBuilder = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();
                var getReaderBuilder    = ctx.GetRequiredService<Func<SystemReaderBuilder>>();

                var processor = getProcessorBuilder()
                    .ProcessorId("connectors-state-projection")
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = loggerFactory,
                        LogName       = "ConnectorsStateProjection"
                    })
                    .DisableAutoLock()
                    .AutoCommit(new AutoCommitOptions {
                        Enabled          = true,
                        RecordsThreshold = 1,
                        StreamTemplate   = "$connectors-mngt/{0}/checkpoints" // internal streams. for control plane should be $connectors-ctrl/{0}/checkpoints
                    })
                    .Filter(ConnectorQueryConventions.Filters.ConnectorsStateProjectionStreamFilter)
                    // .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
                    .InitialPosition(SubscriptionInitialPosition.Earliest)
                    .WithModule(new ConnectorsStateProjection(getReaderBuilder, "$connectors-mngt/connectors-state-projection"))
                    .Create();

                return new ConnectorsStateProjectionService(processor, ctx);
            });
    }
}

class ConnectorsStateProjectionService(IProcessor processor, IServiceProvider serviceProvider)
    : LeadershipAwareProcessorWorker<IProcessor>(processor, serviceProvider);