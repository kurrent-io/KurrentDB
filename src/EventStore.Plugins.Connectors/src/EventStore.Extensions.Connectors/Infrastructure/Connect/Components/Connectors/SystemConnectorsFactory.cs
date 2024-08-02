// ReSharper disable CheckNamespace

using EventStore.Connect.Processors;
using EventStore.Connectors;
using EventStore.Connectors.Http;
using EventStore.Connectors.Kafka;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using AutoLockOptions = EventStore.Streaming.Processors.Configuration.AutoLockOptions;

namespace EventStore.Connect.Connectors;

public record SystemConnectorsFactoryOptions {
    public StreamTemplate LifecycleStreamTemplate   { get; init; } = ConnectorsFeatureConventions.Streams.LifecycleStreamTemplate;
    public StreamTemplate LeasesStreamTemplate      { get; init; } = ConnectorsFeatureConventions.Streams.LeasesStreamTemplate;
    public StreamTemplate CheckpointsStreamTemplate { get; init; } = ConnectorsFeatureConventions.Streams.CheckpointsStreamTemplate;
}

public class SystemConnectorsFactory(SystemConnectorsFactoryOptions options, IConnectorValidator validation, IServiceProvider services) : IConnectorFactory {
    public SystemConnectorsFactory(IServiceProvider services)
        : this(
            services.GetRequiredService<SystemConnectorsFactoryOptions>(),
            services.GetRequiredService<SystemConnectorsValidation>(),
            services
        ) { }

    SystemConnectorsFactoryOptions Options    { get; } = options;
    IConnectorValidator            Validation { get; } = validation;
    IServiceProvider               Services   { get; } = services;

    public IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration, string? ownerId = null) {
        var options = configuration.GetRequiredOptions<SinkOptions>();

        Validation.EnsureValid(configuration);

        var sinkProxy = new SinkProxy(
            connectorId,
            CreateSink(options.InstanceTypeName),
            configuration,
            Services
        );

        var processor = ConfigureProcessor(connectorId, options, sinkProxy, ownerId);

        return new SinkConnector(processor);

        static ISink CreateSink(string connectorTypeName) {
            try {
                return connectorTypeName switch {
                    _ when connectorTypeName == typeof(HttpSink).FullName   => new HttpSink(),
                    _ when connectorTypeName == typeof(KafkaSink).FullName  => new KafkaSink(),
                    _ when connectorTypeName == typeof(LoggerSink).FullName => new LoggerSink(),
                    _                                                       => throw new($"Failed to find sink {connectorTypeName}")
                };
            }
            catch (Exception ex) {
                throw new($"Failed to create sink {connectorTypeName}", ex);
            }
        }
    }

    IProcessor ConfigureProcessor(ConnectorId connectorId, SinkOptions sinkOptions, SinkProxy sinkProxy, string? ownerId = null) {
        var publisher      = Services.GetRequiredService<IPublisher>();
        var loggerFactory  = Services.GetRequiredService<ILoggerFactory>();
        var schemaRegistry = Services.GetRequiredService<SchemaRegistry>();
        var stateStore     = Services.GetRequiredService<IStateStore>();

        var filter = sinkOptions.Subscription.Filter.Scope == SinkConsumeFilterScope.Unspecified
            ? ConsumeFilter.None
            : string.IsNullOrWhiteSpace(sinkOptions.Subscription.Filter.Expression)
                ? ConsumeFilter.ExcludeSystemEvents()
                : ConsumeFilter.From((ConsumeFilterScope)sinkOptions.Subscription.Filter.Scope,
                    sinkOptions.Subscription.Filter.Expression);

        var autoCommitOptions = new AutoCommitOptions {
            Enabled          = sinkOptions.AutoCommit.Enabled,
            Interval         = TimeSpan.FromMilliseconds(sinkOptions.AutoCommit.Interval),
            RecordsThreshold = sinkOptions.AutoCommit.RecordsThreshold,
            StreamTemplate   = Options.CheckpointsStreamTemplate
        };

        var autoLockOptions = new AutoLockOptions {
            Enabled            = sinkOptions.AutoLock.Enabled,
            OwnerId            = ownerId ?? sinkOptions.AutoLock.OwnerId, //TODO SS: one or the other has to give...
            LeaseDuration      = sinkOptions.AutoLock.LeaseDuration,
            AcquisitionTimeout = sinkOptions.AutoLock.AcquisitionTimeout,
            AcquisitionDelay   = sinkOptions.AutoLock.AcquisitionDelay,
            StreamTemplate     = Options.LeasesStreamTemplate
        };

        var loggingOptions = new EventStore.Streaming.Configuration.LoggingOptions {
            Enabled       = sinkOptions.Logging.Enabled,
            LogName       = "EventStore.Connect.Data.SinkConnector",
            LoggerFactory = loggerFactory
        };

        var publishStateChangesOptions = new PublishStateChangesOptions {
            Enabled        = true,
            StreamTemplate = Options.LifecycleStreamTemplate
        };

        var builder = SystemProcessor.Builder
            .ProcessorId(connectorId)
            .SubscriptionName(sinkOptions.Subscription.SubscriptionName)
            .Publisher(publisher)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .Filter(filter)
            .Logging(loggingOptions)
            .StartPosition(sinkOptions.Subscription.StartPosition, sinkOptions.Subscription.ResetPosition)
            .PublishStateChanges(publishStateChangesOptions)
            .AutoCommit(autoCommitOptions)
            .AutoLock(autoLockOptions)
            .SkipDecoding()
            .WithHandler(sinkProxy);

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

    sealed class SinkConnector(IProcessor processor) : IConnector {
        public ConnectorId    ConnectorId { get; } = ConnectorId.From(processor.ProcessorId);
        public ConnectorState State       { get; } = (ConnectorState)processor.State;

        public Task Stopped => processor.Stopped;

        public Task Connect(CancellationToken stoppingToken) =>
            processor.Activate(stoppingToken);

        public ValueTask DisposeAsync() =>
            processor.DisposeAsync();
    }
}