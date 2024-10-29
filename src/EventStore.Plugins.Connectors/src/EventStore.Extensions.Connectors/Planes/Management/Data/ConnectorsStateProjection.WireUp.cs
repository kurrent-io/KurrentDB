using EventStore.Connect.Processors.Configuration;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.System;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.Queries.ConnectorQueryConventions.Filters;
using static EventStore.Connectors.Management.Queries.ConnectorQueryConventions.Streams;

namespace EventStore.Connectors.Management.Data;

static class ConnectorsStateProjectorWireUp {
    public static IServiceCollection AddConnectorsStateProjection(this IServiceCollection services) {
        return services
           .AddSingleton<IHostedService, ConnectorsStateProjectionService>(ctx => {
               const string logName = "ConnectorsStateProjection";

               return new ConnectorsStateProjectionService(() => {
                   var loggerFactory       = ctx.GetRequiredService<ILoggerFactory>();
                   var getProcessorBuilder = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();
                   var getReaderBuilder    = ctx.GetRequiredService<Func<SystemReaderBuilder>>();
                   var getProducerBuilder  = ctx.GetRequiredService<Func<SystemProducerBuilder>>();

                   var processor = getProcessorBuilder()
                       .ProcessorId("connectors-mngt-state-pjx")
                       .Logging(new LoggingOptions {
                           Enabled       = true,
                           LoggerFactory = loggerFactory,
                           LogName       = "ConnectorsStateProjection"
                       })
                       .DisableAutoLock()
                       .AutoCommit(new AutoCommitOptions {
                           Enabled          = true,
                           RecordsThreshold = 1,
                           StreamTemplate   = ConnectorsStateProjectionCheckpointsStream.ToString()
                       })
                       .Filter(ConnectorsStateProjectionStreamFilter)
                       .PublishStateChanges(new PublishStateChangesOptions { Enabled = false })
                       .InitialPosition(SubscriptionInitialPosition.Earliest)
                       .WithModule(new ConnectorsStateProjection(
                           getReaderBuilder,
                           getProducerBuilder,
                           ConnectorsStateProjectionStream))
                       .Create();

                   return processor;
                }, ctx, logName);
            });
    }
}

class ConnectorsStateProjectionService(Func<IProcessor> getProcessor, IServiceProvider serviceProvider, string name)
    : LeadershipAwareProcessorWorker<IProcessor>(getProcessor, serviceProvider, name);