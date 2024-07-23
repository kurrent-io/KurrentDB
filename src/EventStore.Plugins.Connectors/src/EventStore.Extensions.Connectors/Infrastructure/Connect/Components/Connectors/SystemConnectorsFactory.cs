// ReSharper disable CheckNamespace

using EventStore.Connect.Processors;
using EventStore.Connectors;
using EventStore.Connectors.Kafka;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using AutoLockOptions = EventStore.Streaming.Processors.Configuration.AutoLockOptions;

namespace EventStore.Connect.Connectors;

public record SystemConnectorsFactoryOptions {
    public StreamTemplate LifecycleStreamTemplate   { get; init; } = ConnectorsSystemConventions.Streams.LifecycleStreamTemplate;
    public StreamTemplate LeasesStreamTemplate      { get; init; } = ConnectorsSystemConventions.Streams.LeasesStreamTemplate;
    public StreamTemplate CheckpointsStreamTemplate { get; init; } = ConnectorsSystemConventions.Streams.CheckpointsStreamTemplate;
}

public class SystemConnectorsFactory(SystemConnectorsFactoryOptions options, IConnectorValidator validation, IServiceProvider services) : IConnectorFactory {
    public SystemConnectorsFactory(IServiceProvider services)
        : this(
            services.GetRequiredService<SystemConnectorsFactoryOptions>(),
            services.GetRequiredService<SystemConnectorsValidation>(),
            services
        ) { }

    SystemConnectorsFactoryOptions Options    { get; } = options;
    IConnectorValidator                Validation { get; } = validation;
    IServiceProvider                   Services   { get; } = services;

    public IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration, string? ownerId = null) {
        var options = configuration.GetRequiredOptions<SinkOptions>();

        Validation.EnsureValid(configuration);

        var sinkProxy = new SinkProxy(
            connectorId,
            CreateSink(options.InstanceTypeName),
            configuration,
            Services
        );

        var processor = ConfigureProcessor(connectorId, options, sinkProxy);

        return new SinkConnector(processor);

        static ISink CreateSink(string connectorTypeName) {
            try {
                return connectorTypeName switch {
                    // _ when connectorTypeName == typeof(HttpSink).FullName  => new HttpSink(),
                    _ when connectorTypeName == typeof(KafkaSink).FullName => new KafkaSink(),
                    _                                                      => throw new($"Failed to find sink {connectorTypeName}")
                };
            }
            catch (Exception ex) {
                throw new($"Failed to create sink {connectorTypeName}", ex);
            }
        }
    }

    IProcessor ConfigureProcessor(ConnectorId connectorId, SinkOptions options, SinkProxy sinkProxy, string? ownerId = null) {
        var publisher      = Services.GetRequiredService<IPublisher>();
        var loggerFactory  = Services.GetRequiredService<ILoggerFactory>();
        var schemaRegistry = Services.GetRequiredService<SchemaRegistry>();
        var stateStore     = Services.GetRequiredService<IStateStore>();

        var filter = options.Subscription.ConsumeFilter.Scope == SinkConsumeFilterScope.Unspecified
            ? ConsumeFilter.None
            : ConsumeFilter.From(
                (ConsumeFilterScope)options.Subscription.ConsumeFilter.Scope,
                options.Subscription.ConsumeFilter.Expression
            );

        var autoCommitOptions = new AutoCommitOptions {
            Enabled          = options.AutoCommit.Enabled,
            Interval         = TimeSpan.FromMilliseconds(options.AutoCommit.Interval),
            RecordsThreshold = options.AutoCommit.RecordsThreshold,
            StreamTemplate   = Options.CheckpointsStreamTemplate
        };

        var autoLockOptions = new AutoLockOptions {
            Enabled            = options.AutoLock.AutoLockEnabled,
            OwnerId            = ownerId ?? options.AutoLock.OwnerId, //TODO SS: one or the other has to give...
            LeaseDuration      = options.AutoLock.LeaseDuration,
            AcquisitionTimeout = options.AutoLock.AcquisitionTimeout,
            AcquisitionDelay   = options.AutoLock.AcquisitionDelay,
            StreamTemplate     = Options.LeasesStreamTemplate
        };

        var loggingOptions = new Streaming.Configuration.LoggingOptions {
            Enabled       = options.Logging.Enabled,
            LogName       = "EventStore.Connect.Data.SystemSinkConnector",
            LoggerFactory = loggerFactory
        };

        var publishStateChangesOptions = new PublishStateChangesOptions {
            Enabled        = true,
            StreamTemplate = Options.LifecycleStreamTemplate
        };

        var builder = SystemProcessor.Builder
            .ProcessorId(connectorId)
            .SubscriptionName(options.Subscription.SubscriptionName)
            .Publisher(publisher)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .Filter(filter)
            .Logging(loggingOptions)
            .StartPosition(RecordPosition.Latest)
            .PublishStateChanges(publishStateChangesOptions)
            .AutoCommit(autoCommitOptions)
            .AutoLock(autoLockOptions)
            .SkipDecoding()
            .WithHandler(sinkProxy);

        // TODO SS: needs to let the consumer handle the checkpoint retrieval while still accepting the user provided log position, but how?

        // (ulong? commitPosition, ulong? preparePosition)
        // null, null is the earliest position instead of 0,0 ?
        // I need a VOID/NULL/UNSET position, and if null already means something and the type is ulong I'm fucked
        // the absence of a value as a whole fixes it, but then I need to play with nulls on a higher level than I want too... LogPosition?
        //
        // if I have a start position, then subscribe from there,
        // if not, load the checkpoint and start from there,
        // but if there was no checkpoint...its either Earliest or Latest.

        if (options.Subscription.StartFromLogPosition != null) {
            builder = builder.StartPosition(options.Subscription.StartFromLogPosition);
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
            // OMG OMG OMG OMG do I need to get back InitialPosition?!?!? if startposition null, and no checkpoint then initialposition FML FML FML FML FML FML
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