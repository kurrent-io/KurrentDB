// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

using EventStore.Connect.Processors;
using EventStore.Connectors.EventStoreDB;
using EventStore.Connectors;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
using EventStore.Connectors.Mongo;
using EventStore.Connectors.RabbitMQ;
using EventStore.Connectors.System;
using EventStore.Connectors.Testing;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.JavaScript;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Transformers;
using Humanizer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using AutoLockOptions = EventStore.Streaming.Processors.Configuration.AutoLockOptions;

namespace EventStore.Connect.Connectors;

public record SystemConnectorsFactoryOptions {
    public StreamTemplate             CheckpointsStreamTemplate { get; init; } = ConnectorsFeatureConventions.Streams.CheckpointsStreamTemplate;
    public StreamTemplate             LifecycleStreamTemplate   { get; init; } = ConnectorsFeatureConventions.Streams.LifecycleStreamTemplate;
    public AutoLockOptions            AutoLock                  { get; init; } = new();
}

public class SystemConnectorsFactory(SystemConnectorsFactoryOptions options, IConnectorValidator validation, IServiceProvider services) : IConnectorFactory {
    SystemConnectorsFactoryOptions Options    { get; } = options;
    IConnectorValidator            Validation { get; } = validation;
    IServiceProvider               Services   { get; } = services;

    static readonly Dictionary<string, Func<ISink>> SinkInstanceTypes = new() {
        { typeof(HttpSink).FullName!, () => new HttpSink() },
        { nameof(HttpSink), () => new HttpSink() },
        { nameof(HttpSink).Kebaberize(), () => new HttpSink() },

        { nameof(KafkaSink), () => new KafkaSink() },
        { typeof(KafkaSink).FullName!, () => new KafkaSink() },
        { nameof(KafkaSink).Kebaberize(), () => new KafkaSink() },

        { nameof(LoggerSink), () => new LoggerSink() },
        { typeof(LoggerSink).FullName!, () => new LoggerSink() },
        { nameof(LoggerSink).Kebaberize(), () => new LoggerSink() },

        { nameof(RabbitMqSink), () => new RabbitMqSink() },
        { typeof(RabbitMqSink).FullName!, () => new RabbitMqSink() },
        { nameof(RabbitMqSink).Kebaberize(), () => new RabbitMqSink() },

        { nameof(EventStoreDBSink), () => new EventStoreDBSink() },
        { typeof(EventStoreDBSink).FullName!, () => new EventStoreDBSink() },
        { nameof(EventStoreDBSink).Kebaberize(), () => new EventStoreDBSink() },

        { nameof(MongoDbSink), () => new MongoDbSink() },
        { typeof(MongoDbSink).FullName!, () => new MongoDbSink() },
        { nameof(MongoDbSink).Kebaberize(), () => new MongoDbSink() },
    };

    public IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration) {
        var sinkOptions = configuration.GetRequiredOptions<SinkOptions>();

        Validation.EnsureValid(configuration);

        var sinkProxy = new SinkProxy(
            connectorId,
            CreateSink(sinkOptions.InstanceTypeName),
            configuration,
            Services
        );

        var processor = ConfigureProcessor(connectorId, sinkOptions, sinkProxy);

        return new SinkConnector(processor, sinkProxy);

        static ISink CreateSink(string connectorTypeName) {
            if (SinkInstanceTypes.TryGetValue(connectorTypeName, out var instance))
                return instance();

            throw new ArgumentException($"Failed to find sink {connectorTypeName}", nameof(connectorTypeName));
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

        var nodeSysInfoProvider = Services.GetRequiredService<INodeSystemInfoProvider>();

        var nodeId = nodeSysInfoProvider
            .GetNodeSystemInfo().GetAwaiter().GetResult()
            .InstanceId.ToString();

        var filter = sinkOptions.Subscription.Filter.Scope == SinkConsumeFilterScope.Unspecified
            ? ConsumeFilter.None
            : string.IsNullOrWhiteSpace(sinkOptions.Subscription.Filter.Expression)
                ? ConsumeFilter.ExcludeSystemEvents()
                : ConsumeFilter.From(
                    (ConsumeFilterScope)sinkOptions.Subscription.Filter.Scope,
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

        if (sinkOptions.Transformer.Enabled) {
            builder = builder.Transformer(
                Transformer.Builder
                    .Transform(
                        JsFunction.Builder
                            .Function(sinkOptions.Transformer.Function)
                            .FunctionName(sinkOptions.Transformer.FunctionName)
                            .ExecutionTimeoutMs(sinkOptions.Transformer.ExecutionTimeoutMs)
                            .LoggerFactory(loggerFactory)
                            .Create()
                    )
                    .Logging(loggingOptions)
                    .SchemaRegistry(schemaRegistry)
                    .Create()
            );
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