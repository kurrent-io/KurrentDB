using EventStore.Connect.Processors;
using EventStore.Connectors.System;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.ConnectorsFeatureConventions.Streams;

namespace EventStore.Connectors.Management.Reactors;

static class ConnectorsLifecycleReactorWireUp {
    public static IServiceCollection AddConnectorsLifecycleReactor(this IServiceCollection services) {
        return services.AddSingleton<IHostedService, ConnectorsLifecycleReactorService>(ctx => {
            var app            = ctx.GetRequiredService<ConnectorsCommandApplication>();
            var publisher      = ctx.GetRequiredService<IPublisher>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();

            const string logName = "ConnectorsLifecycleReactor";

            var processor = SystemProcessor.Builder
                .ProcessorId("connectors-mngt-lifecycle-rx")
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .Logging(new LoggingOptions {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = logName
                })
                .DisableAutoLock()
                .AutoCommit(new AutoCommitOptions {
                    Enabled          = true,
                    RecordsThreshold = 100,
                    StreamTemplate   = ManagementLifecycleReactorCheckpointsStream.ToString()
                })
                .PublishStateChanges(new PublishStateChangesOptions { Enabled = false })
                .InitialPosition(SubscriptionInitialPosition.Earliest)
                .Filter(ConnectorsFeatureConventions.Filters.LifecycleFilter)
                .WithHandler(new ConnectorsLifecycleReactor(app))
                .Create();

            return new ConnectorsLifecycleReactorService(processor, ctx, logName);
        });
    }
}

class ConnectorsLifecycleReactorService(IProcessor processor, IServiceProvider serviceProvider, string name)
    : LeadershipAwareProcessorWorker<IProcessor>(processor, serviceProvider, name);