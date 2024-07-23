using EventStore.Connect.Processors;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Hosting;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Reactors;

static class ConnectorsStreamSupervisorWireUp {
    public static IServiceCollection AddConnectorsStreamSupervisor(this IServiceCollection services) {
        return services.AddSingleton<IHostedService>(
            ctx => {
                var publisher      = ctx.GetRequiredService<IPublisher>();
                var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
                var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();

                var options = new ConnectorSystemStreamsOptions {
                    Leases      = new(MaxCount: 1),
                    Checkpoints = new(MaxCount: 3),
                    Lifetime    = new(MaxCount: 5)
                };

                var processor = SystemProcessor.Builder
                    .ProcessorId("connectors-streams-supervisor-rx")
                    .Publisher(publisher)
                    .SchemaRegistry(schemaRegistry)
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = loggerFactory,
                        LogName       = "ConnectorsStreamSupervisor"
                    })
                    .DisableAutoLock()
                    .DisableAutoCommit()
                    .StartPosition(RecordPosition.Latest)
                    .Filter(ConnectorsSystemConventions.Filters.LifecycleFilter)
                    .WithModule(new ConnectorsStreamSupervisor(publisher, options))
                    .Create();

                return new ConnectorsStreamSupervisorService(processor, ctx);
            }
        );
    }
}

class ConnectorsStreamSupervisorService(IProcessor processor, IServiceProvider serviceProvider)
    : LeadershipAwareProcessorWorker<IProcessor>(processor, serviceProvider);