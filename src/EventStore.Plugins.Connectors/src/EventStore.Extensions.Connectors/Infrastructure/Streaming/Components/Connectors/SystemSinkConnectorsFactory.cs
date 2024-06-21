// ReSharper disable CheckNamespace

using EventStore.Connect.Connectors;
using EventStore.Connect.Connectors.Sinks;
using EventStore.Core.Bus;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Streaming.Connectors;

public class SystemSinkConnectorsFactory(IServiceProvider services) : SinkConnectorFactory(services) {
    protected override IProcessor ConfigureProcessor(ConnectorId connectorId, SinkOptions options, SinkProxy sinkProxy) {
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
            .StartPosition(options.Subscription.StartFromRecordPosition)
            .LogPosition(options.Subscription.StartFromLogPosition)
            .AutoCommit(options.AutoCommit.Interval, options.AutoCommit.RecordsThreshold)
            .LoggerFactory(loggerFactory)
            .EnableLogging(options.Logging.Enabled)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .SkipDecoding()
            .WithHandler(sinkProxy);

        return builder.Create();
    }
}