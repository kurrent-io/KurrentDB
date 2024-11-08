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
        const string serviceName = "ConnectorsLifecycleReactor";

        return services.AddSingleton<IHostedService, ConnectorsLifecycleReactorService>(ctx => {
            return new ConnectorsLifecycleReactorService(() => {
                var app            = ctx.GetRequiredService<ConnectorsCommandApplication>();
                var publisher      = ctx.GetRequiredService<IPublisher>();
                var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
                var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();

                var processor = SystemProcessor.Builder
                    .ProcessorId(serviceName)
                    .Publisher(publisher)
                    .SchemaRegistry(schemaRegistry)
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = loggerFactory,
                        LogName       = "EventStore.Connect.Processors.SystemProcessor"
                    })
                    .DisableAutoLock()
                    .AutoCommit(new AutoCommitOptions {
                        Enabled          = true,
                        RecordsThreshold = 100,
                        StreamTemplate   = ManagementLifecycleReactorCheckpointsStream
                    })
                    .PublishStateChanges(new PublishStateChangesOptions { Enabled = false })
                    .InitialPosition(SubscriptionInitialPosition.Earliest)
                    .Filter(ConnectorsFeatureConventions.Filters.LifecycleFilter)
                    .WithHandler(new ConnectorsLifecycleReactor(app))
                    .Create();

                return processor;
            }, ctx, serviceName);
        });
    }
}

class ConnectorsLifecycleReactorService(Func<IProcessor> getProcessor, IServiceProvider serviceProvider, string serviceName)
    : LeaderNodeProcessorWorker<IProcessor>(getProcessor, serviceProvider, serviceName);