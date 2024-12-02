// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

using EventStore.Connect.Processors;
using EventStore.Connectors;
using EventStore.Connectors.Connect.Components.Connectors;
using EventStore.Connectors.System;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Transformers;
using EventStore.Toolkit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutoLockOptions = EventStore.Streaming.Processors.Configuration.AutoLockOptions;

namespace EventStore.Connect.Connectors;

public record SystemConnectorsFactoryOptions {
    public StreamTemplate  CheckpointsStreamTemplate { get; init; } = ConnectorsFeatureConventions.Streams.CheckpointsStreamTemplate;
    public StreamTemplate  LifecycleStreamTemplate   { get; init; } = ConnectorsFeatureConventions.Streams.LifecycleStreamTemplate;
    public AutoLockOptions AutoLock                  { get; init; } = new();
}

public class SystemConnectorsFactory(
    SystemConnectorsFactoryOptions options,
    IConnectorValidator validation,
    IServiceProvider services
) : IConnectorFactory {
    SystemConnectorsFactoryOptions Options    { get; } = options;
    IConnectorValidator            Validation { get; } = validation;
    IServiceProvider               Services   { get; } = services;

    public IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration) {
        var sinkOptions = configuration.GetRequiredOptions<SinkOptions>();

        Validation.EnsureValid(configuration);

        var sink = CreateSink(sinkOptions.InstanceTypeName);

        if (sinkOptions.Transformer.Enabled)
            sink = new RecordTransformerSink(sink, new JintRecordTransformer(sinkOptions.Transformer.DecodeFunction()));

        var sinkProxy = new SinkProxy(connectorId,
            sink,
            configuration,
            Services);

        var processor = ConfigureProcessor(connectorId, sinkOptions, sinkProxy);

        return new SinkConnector(processor, sinkProxy);

        static ISink CreateSink(string connectorTypeName) {
            if (!ConnectorCatalogue.TryGetConnector(connectorTypeName, out var connector))
                throw new ArgumentException($"Failed to find sink {connectorTypeName}", nameof(connectorTypeName));

            return (Activator.CreateInstance(connector.ConnectorType) as ISink)!;
        }
    }

    IProcessor ConfigureProcessor(ConnectorId connectorId, SinkOptions sinkOptions, SinkProxy sinkProxy) {
        var publisher      = Services.GetRequiredService<IPublisher>();
        var loggerFactory  = Services.GetRequiredService<ILoggerFactory>();
        var schemaRegistry = Services.GetRequiredService<SchemaRegistry>();
        var stateStore     = Services.GetRequiredService<IStateStore>();

        // TODO SS: seriously, this is a bad idea, but creating a connector to be hosted in ESDB or PaaS is different from having full control of the Connect framework

        // It was either this, or having a separate configuration for the node or
        // even creating a factory provider that would create a new factory when the node becomes a leader,
        // and then it escalates because it would be the activator that would need to be recreated to "hide"
        // all this mess. Maybe these options would be passed on Create method and not on the factory...
        // I don't know, I'm just trying to make it work.
        // I'm not happy with this, but it's the best I could come up with in the time I had.

        var getNodeSystemInfo = Services.GetRequiredService<GetNodeSystemInfo>();

        var nodeId = getNodeSystemInfo().AsTask().GetAwaiter().GetResult().InstanceId.ToString();

        var filter = string.IsNullOrWhiteSpace(sinkOptions.Subscription.Filter.Expression)
            ? sinkOptions.Subscription.Filter.Scope is SinkConsumeFilterScope.Unspecified
                ? ConsumeFilter.ExcludeSystemEvents()
                : ConsumeFilter.None
            : ConsumeFilter.From(
                (ConsumeFilterScope)sinkOptions.Subscription.Filter.Scope,
                (ConsumeFilterType)sinkOptions.Subscription.Filter.FilterType,
                sinkOptions.Subscription.Filter.Expression
            );

        var publishStateChangesOptions = new PublishStateChangesOptions {
            Enabled        = true,
            StreamTemplate = Options.LifecycleStreamTemplate
        };

        var autoLockOptions = Options.AutoLock with { OwnerId = nodeId };

        var autoCommitOptions = new AutoCommitOptions {
            Enabled          = sinkOptions.AutoCommit.Enabled,
            Interval         = TimeSpan.FromMilliseconds(sinkOptions.AutoCommit.Interval),
            RecordsThreshold = sinkOptions.AutoCommit.RecordsThreshold,
            StreamTemplate   = Options.CheckpointsStreamTemplate
        };

        var loggingOptions = new EventStore.Streaming.Configuration.LoggingOptions {
            Enabled       = sinkOptions.Logging.Enabled,
            LogName       = sinkOptions.InstanceTypeName,
            LoggerFactory = loggerFactory
        };

        var builder = SystemProcessor.Builder
            .ProcessorId(connectorId)
            .Publisher(publisher)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .InitialPosition(sinkOptions.Subscription.InitialPosition)
            .PublishStateChanges(publishStateChangesOptions)
            .AutoLock(autoLockOptions)
            .Filter(filter)
            .Logging(loggingOptions)
            .AutoCommit(autoCommitOptions)
            .SkipDecoding()
            .WithHandler(sinkProxy);

        if (sinkOptions.Subscription.StartPosition is not null
         && sinkOptions.Subscription.StartPosition != RecordPosition.Unset
         && sinkOptions.Subscription.StartPosition != LogPosition.Unset) {
            builder = builder.StartPosition(sinkOptions.Subscription.StartPosition);
        }

        return builder.Create();
    }

    sealed class SinkConnector(IProcessor processor, SinkProxy sinkProxy) : IConnector {
        public ConnectorId    ConnectorId { get; } = ConnectorId.From(processor.ProcessorId);
        public ConnectorState State       { get; } = (ConnectorState)processor.State;

        public Task Stopped => processor.Stopped;

        public async Task Connect(CancellationToken stoppingToken) {
            await sinkProxy.Initialize(stoppingToken);
            await processor.Activate(stoppingToken);
        }

        public ValueTask DisposeAsync() =>
            processor.DisposeAsync();
    }
}