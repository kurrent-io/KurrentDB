using EventStore.Connect.Processors.Configuration;
using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.System;
using Humanizer;
using Kurrent.Surge;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using static EventStore.Connectors.Management.Queries.ConnectorQueryConventions.Streams;

namespace EventStore.Connectors.Management.Data;

static class ConnectorsStateProjectorWireUp {
    public static IServiceCollection AddConnectorsStateProjection(this IServiceCollection services) {
        const string serviceName = "ConnectorsStateProjection";

        services.AddSingleton(ctx => {
            var projectionsStore = ctx.GetRequiredService<ISnapshotProjectionsStore>();
            return new ConnectorsStateProjection(projectionsStore, ConnectorsStateProjectionStream);
        });

        services.AddSingleton<IConnectorsStateProjection>(ctx => ctx.GetRequiredService<ConnectorsStateProjection>());

        return services
           .AddSingleton<IHostedService, ConnectorsStateProjectionService>(ctx => {
               return new ConnectorsStateProjectionService(() => {
                   var loggerFactory         = ctx.GetRequiredService<ILoggerFactory>();
                   var getProcessorBuilder   = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();
                   var stateProjectionModule = ctx.GetRequiredService<ConnectorsStateProjection>();

                   var processor = getProcessorBuilder()
                       .ProcessorId(serviceName)
                       .Logging(new LoggingOptions {
                           Enabled       = true,
                           LoggerFactory = loggerFactory,
                           LogName       = "Kurrent.Surge.Processors.SystemProcessor"
                       })
                       .DisableAutoLock()
                       .AutoCommit(new AutoCommitOptions {
                           Enabled          = true,
                           RecordsThreshold = 1000,
                           Interval         = 5.Seconds(),
                           StreamTemplate   = ConnectorsStateProjectionCheckpointsStream
                       })
                       .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
                       .DisablePublishStateChanges()
                       .InitialPosition(SubscriptionInitialPosition.Latest)
                       .WithModule(stateProjectionModule)
                       .Create();

                   return processor;
                }, ctx, serviceName);
            });
    }
}

class ConnectorsStateProjectionService(Func<IProcessor> getProcessor, IServiceProvider serviceProvider, string serviceName)
    : LeaderNodeProcessorWorker<IProcessor>(getProcessor, serviceProvider, serviceName);