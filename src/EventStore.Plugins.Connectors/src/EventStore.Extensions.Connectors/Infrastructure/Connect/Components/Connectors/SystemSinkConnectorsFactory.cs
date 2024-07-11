// ReSharper disable CheckNamespace

using EventStore.Connect.Connectors.Sinks;
using EventStore.Connect.Processors;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Core.Bus;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutoLockOptions = EventStore.Streaming.Processors.Configuration.AutoLockOptions;

namespace EventStore.Connect.Connectors;

public class SystemSinkConnectorsFactory(IServiceProvider services) : SinkConnectorFactory(services) {
    protected override IProcessor ConfigureProcessor(ConnectorId connectorId, SinkOptions options, SinkProxy sinkProxy, string? ownerId = null) {
        var client         = Services.GetRequiredService<IPublisher>();
        var loggerFactory  = Services.GetRequiredService<ILoggerFactory>();
        var schemaRegistry = Services.GetRequiredService<SchemaRegistry>();
        var stateStore     = Services.GetRequiredService<IStateStore>();

        var filter = ConsumeFilter.From(
            (ConsumeFilterScope)options.Subscription.ConsumeFilter.Scope,
            options.Subscription.ConsumeFilter.Expression
        );

        var builder = SystemProcessor.Builder
            .ProcessorId(connectorId)
            .SubscriptionName(options.Subscription.SubscriptionName)
            .Publisher(client)
            .Filter(filter)
            .InitialPosition((SubscriptionInitialPosition)options.Subscription.InitialPosition)
            .StartPosition(options.Subscription.StartFromLogPosition)
            .AutoCommit(options.AutoCommit.Interval, options.AutoCommit.RecordsThreshold)
            .AutoLock(new AutoLockOptions {
                    AutoLockEnabled    = options.AutoLock.AutoLockEnabled,
                    OwnerId            = ownerId ?? options.AutoLock.OwnerId,
                    LeaseDuration      = options.AutoLock.LeaseDuration,
                    AcquisitionTimeout = options.AutoLock.AcquisitionTimeout,
                    AcquisitionDelay   = options.AutoLock.AcquisitionDelay,
                    // StreamNamespace    = "" // TODO SS: this should be set by the connector
                }
            )
            .LoggerFactory(loggerFactory)
            .EnableLogging(options.Logging.Enabled)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .SkipDecoding()
            .WithHandler(sinkProxy);

        return builder.Create();
    }
}