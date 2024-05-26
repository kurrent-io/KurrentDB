// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.IO.Connectors;
using EventStore.IO.Connectors.Configuration;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Streaming.Connectors;

public delegate IProcessor CreateConnectorInstance(string connectorId, Dictionary<string, string?> settings);

[PublicAPI]
public record SystemConnectorsFactory(IServiceProvider ServiceProvider) : IConnectorsFactory {
    public IProcessor Create(string connectorId, IConfiguration configuration) {
        Ensure.NotNullOrWhiteSpace(connectorId);
        Ensure.NotNull(configuration);
        
        var options = configuration.Get<SinkOptions>();

        if (options is null)
            throw new("Sink configuration not found.");
        
        if (string.IsNullOrWhiteSpace(options.SinkTypeName))
            throw new("Sink type name not found.");

        var sinkProxy = new SinkProxy(connectorId, CreateSink(options.SinkTypeName), configuration, ServiceProvider)
            .With(x => x.Initialize());

        var clientSettings = ServiceProvider.GetRequiredService<IPublisher>();
        var loggerFactory  = ServiceProvider.GetRequiredService<ILoggerFactory>();
        var schemaRegistry = ServiceProvider.GetRequiredService<SchemaRegistry>();
        var stateStore     = ServiceProvider.GetRequiredService<IStateStore>();
        
        var builder = SystemProcessor.Builder
            .OverrideProcessorId(connectorId)
            .ProcessorName(connectorId)
            .SubscriptionName(options.Subscription.SubscriptionName)
            .Publisher(clientSettings)
            .Filter(
                ConsumeFilter.From(
                    (ConsumeFilterScope)options.Subscription.ConsumeFilter.Scope,
                    options.Subscription.ConsumeFilter.Expression
                )
            )
            .InitialPosition((SubscriptionInitialPosition)options.Subscription.InitialPosition)
            .AutoCommit(options.AutoCommit.Interval, options.AutoCommit.RecordsThreshold)
            .LoggerFactory(loggerFactory)
            .EnableLogging(options.Logging.Enabled)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .SkipDecoding()
            .WithHandler(sinkProxy);

        return builder.Create();

        static ISink CreateSink(string sinkTypeName) {
            try {
                return (ISink)Activator.CreateInstance(Type.GetType(sinkTypeName)!)!;
            }
            catch (Exception ex) {
                throw new($"Failed to create sink {sinkTypeName}.", ex);
            }
        }
    }
}