using EventStore.Connect.Processors.Configuration;
using EventStore.Connectors.System;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Reactors;

static class ConnectorsStreamSupervisorWireUp {
    public static IServiceCollection AddConnectorsStreamSupervisor(this IServiceCollection services) {
        return services.AddSingleton<IHostedService, ConnectorsStreamSupervisorService>(ctx => {
            var publisher           = ctx.GetRequiredService<IPublisher>();
            var loggerFactory       = ctx.GetRequiredService<ILoggerFactory>();
            var getProcessorBuilder = ctx.GetRequiredService<Func<SystemProcessorBuilder>>();

            var options = new ConnectorsStreamSupervisorOptions {
                Leases      = new(MaxCount: 1),
                Checkpoints = new(MaxCount: 1),
                Lifetime    = new(MaxCount: 5)
            };

            var processor = getProcessorBuilder()
                .ProcessorId("connectors-streams-supervisor-rx")
                .Logging(new LoggingOptions {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "ConnectorsStreamSupervisor"
                })
                .DisableAutoLock()
                .DisableAutoCommit()
                .PublishStateChanges(new PublishStateChangesOptions { Enabled = false })
                .StartPosition(RecordPosition.Latest)
                .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
                .WithModule(new ConnectorsStreamSupervisor(publisher, options))
                .Create();

            return new ConnectorsStreamSupervisorService(processor, ctx);
        });
    }
}

class ConnectorsStreamSupervisorService(IProcessor processor, IServiceProvider serviceProvider)
    : LeadershipAwareProcessorWorker<IProcessor>(processor, serviceProvider);