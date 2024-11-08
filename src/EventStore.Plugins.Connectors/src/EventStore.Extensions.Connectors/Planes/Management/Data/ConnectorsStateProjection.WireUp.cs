using EventStore.Connect.Processors.Configuration;
using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.System;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using static EventStore.Connectors.Management.Queries.ConnectorQueryConventions.Streams;

namespace EventStore.Connectors.Management.Data;

static class ConnectorsStateProjectorWireUp {
    public static IServiceCollection AddConnectorsStateProjection(this IServiceCollection services) {
        const string serviceName = "ConnectorsStateProjection";

        return services
           .AddSingleton<IHostedService, ConnectorsStateProjectionService>(ctx => {
               return new ConnectorsStateProjectionService(() => {
                   var loggerFactory       = ctx.GetRequiredService<ILoggerFactory>();
                   var getProcessorBuilder = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();
                   var projectionsStore    = ctx.GetRequiredService<ISnapshotProjectionsStore>();

                   var processor = getProcessorBuilder()
                       .ProcessorId(serviceName)
                       .Logging(new LoggingOptions {
                           Enabled       = true,
                           LoggerFactory = loggerFactory,
                           LogName       = "EventStore.Connect.Processors.SystemProcessor"
                       })
                       .DisableAutoLock()
                       .AutoCommit(new AutoCommitOptions {
                           Enabled          = true,
                           RecordsThreshold = 100,
                           StreamTemplate   = ConnectorsStateProjectionCheckpointsStream
                       })
                       .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
                       .PublishStateChanges(new PublishStateChangesOptions { Enabled = false })
                       .InitialPosition(SubscriptionInitialPosition.Earliest)
                       .WithModule(new ConnectorsStateProjection(projectionsStore, ConnectorsStateProjectionStream))
                       .Create();

                   return processor;
                }, ctx, serviceName);
            });
    }
}

class ConnectorsStateProjectionService(Func<IProcessor> getProcessor, IServiceProvider serviceProvider, string serviceName)
    : LeaderNodeProcessorWorker<IProcessor>(getProcessor, serviceProvider, serviceName);