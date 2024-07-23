using EventStore.Connect.Processors;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Hosting;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Reactors;

static class ConnectorsCheckpointReactorWireUp {
    public static IServiceCollection AddConnectorsCheckpointReactor(this IServiceCollection services) =>
        services.AddSingleton<IHostedService>(
            ctx => {
                var app            = ctx.GetRequiredService<ConnectorApplication>();
                var publisher      = ctx.GetRequiredService<IPublisher>();
                var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
                var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();

                var processor = SystemProcessor.Builder
                    .ProcessorId("connectors-checkpoint-rx")
                    .Publisher(publisher)
                    .SchemaRegistry(schemaRegistry)
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = loggerFactory,
                        LogName       = "ConnectorsCheckpointReactor"
                    })
                    .DisableAutoLock()
                    .DisableAutoCommit()
                    .StartPosition(RecordPosition.Latest)
                    .Filter(ConnectorsSystemConventions.Filters.CheckpointsFilter)
                    .WithHandler(new ConnectorsCheckpointReactor(app))
                    .Create();

                return new ConnectorsCheckpointReactorService(processor, ctx);
            }
        );
}

class ConnectorsCheckpointReactorService(IProcessor processor, IServiceProvider serviceProvider)
    : LeadershipAwareProcessorWorker<IProcessor>(processor, serviceProvider);