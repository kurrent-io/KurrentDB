// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

using Kurrent.Surge.Connectors.Sinks;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.Connectors.Diagnostics.Metrics;
using Kurrent.Surge.Connectors.Sinks.Diagnostics.Metrics;
using Kurrent.Surge.Connectors.Sources;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Interceptors;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Transformers;

using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Core;
using KurrentDB.Surge.Processors;
using KurrentDB.Surge.Producers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using AutoLockOptions = Kurrent.Surge.Processors.Configuration.AutoLockOptions;
using ConnectorsFeatureConventions = KurrentDB.Connectors.Planes.ConnectorsFeatureConventions;

namespace KurrentDB.Connect.Connectors;

public record SystemConnectorsFactoryOptions {
    public StreamTemplate                CheckpointsStreamTemplate { get; init; } = ConnectorsFeatureConventions.Streams.CheckpointsStreamTemplate;
    public AutoLockOptions               AutoLock                  { get; init; } = new();
    public LinkedList<InterceptorModule> Interceptors              { get; init; } = [];
}

public class SystemConnectorsFactory(SystemConnectorsFactoryOptions options, IServiceProvider services) : ISystemConnectorFactory {
    SystemConnectorsFactoryOptions Options  { get; } = options;
    IServiceProvider               Services { get; } = services;

    static DisposeCallback? OnDisposeCallback;

    public IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration) {
        var options       = configuration.GetRequiredOptions<ConnectorOptions>();
        var validator     = Services.GetRequiredService<IConnectorValidator>();
        var dataProtector = Services.GetRequiredService<IConnectorDataProtector>();

        var config = dataProtector.Unprotect(configuration);

        validator.EnsureValid(config);

        var connector = CreateConnectorInstance(options.InstanceTypeName);

        return connector switch {
	        ISink   => CreateSinkConnector(),
	        ISource => CreateSourceConnector(),
	        _       => throw new InvalidOperationException($"Unexpected connector type: {connector.GetType().Name}")
        };

        SinkConnector CreateSinkConnector() {
	        var sinkOptions = config.GetRequiredOptions<SinkOptions>();

	        if (sinkOptions.Transformer.Enabled) {
		        var transformer = new JintRecordTransformer(sinkOptions.Transformer.DecodeFunction()) {
			        // ReSharper disable once AccessToModifiedClosure
			        ErrorCallback = errorType => SinkMetrics.TrackTransformError(connectorId, connector.MetricsLabel, errorType)
		        };
		        connector = new RecordTransformerSink(connector, transformer);
	        }

	        ConnectorMetrics.TrackSinkConnectorCreated(connector.GetType(), connectorId);

	        OnDisposeCallback = () => ConnectorMetrics.TrackSinkConnectorClosed(connector.GetType(), connectorId);

	        var sinkProxy = new SinkProxy(connectorId, connector, config, Services);

	        var processor = ConfigureSinkProcessor(connectorId, Options.Interceptors, sinkOptions, sinkProxy);

	        return new SinkConnector(processor, sinkProxy);
        }

        SourceConnector CreateSourceConnector() {
	        var sourceOptions = configuration.GetRequiredOptions<SourceOptions>();

	        ConnectorMetrics.TrackSourceConnectorCreated(connector.GetType(), connectorId);

	        OnDisposeCallback = () => ConnectorMetrics.TrackSourceConnectorClosed(connector.GetType(), connectorId);

	        var sourceProxy = new SourceProxy(connectorId, connector, configuration, Services);

	        var processor = ConfigureSourceProcessor(connectorId, Options.Interceptors, sourceOptions, sourceProxy);

	        return new SourceConnector(connectorId, processor);
        }

        dynamic CreateConnectorInstance(string connectorTypeName) {
            try {
                if (!ConnectorCatalogue.TryGetConnector(connectorTypeName, out var conn))
                    throw new ArgumentException($"Failed to find sink {connectorTypeName}", nameof(connectorTypeName));

                return Activator.CreateInstance(conn.ConnectorType)!;
            }
            catch (Exception ex) {
                throw new($"Failed to create connector {connectorTypeName}", ex);
            }
        }
    }

    IProcessor ConfigureSinkProcessor(ConnectorId connectorId, LinkedList<InterceptorModule> interceptors, SinkOptions sinkOptions, SinkProxy sinkProxy) {
        var client         = Services.GetRequiredService<ISystemClient>();
        var loggerFactory  = Services.GetRequiredService<ILoggerFactory>();
        var schemaRegistry = Services.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
        var stateStore     = Services.GetRequiredService<IStateStore>();

        // TODO SS: seriously, this is a bad idea, but creating a connector to be hosted in ESDB or PaaS is different from having full control of the Surge framework

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

        var autoLockOptions = Options.AutoLock with { OwnerId = nodeId };

        var autoCommitOptions = new AutoCommitOptions {
            Enabled          = sinkOptions.AutoCommit.Enabled,
            Interval         = TimeSpan.FromMilliseconds(sinkOptions.AutoCommit.Interval),
            RecordsThreshold = sinkOptions.AutoCommit.RecordsThreshold,
            StreamTemplate   = Options.CheckpointsStreamTemplate
        };

        var loggingOptions = new Kurrent.Surge.Configuration.LoggingOptions {
            Enabled       = sinkOptions.Logging.Enabled,
            LogName       = sinkOptions.InstanceTypeName,
            LoggerFactory = loggerFactory
        };

        var builder = SystemProcessor.Builder
            .ProcessorId(connectorId)
            .Client(client)
            .StateStore(stateStore)
            .SchemaRegistry(schemaRegistry)
            .InitialPosition(sinkOptions.Subscription.InitialPosition)
            .DisablePublishStateChanges()
            .Interceptors(interceptors)
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

    IProcessor ConfigureSourceProcessor(ConnectorId connectorId, LinkedList<InterceptorModule> interceptors, SourceOptions sourceOptions, SourceProxy sourceProxy) {
	    var client         = Services.GetRequiredService<ISystemClient>();
	    var loggerFactory  = Services.GetRequiredService<ILoggerFactory>();
	    var schemaRegistry = Services.GetRequiredService<SchemaRegistry>();

	    var loggingOptions = new Kurrent.Surge.Configuration.LoggingOptions {
		    Enabled = sourceOptions.Logging.Enabled,
		    LogName = sourceOptions.InstanceTypeName,
		    LoggerFactory = loggerFactory
	    };

	    var producer = SystemProducer.Builder
		    .Client(client)
		    .ClientId(connectorId)
		    .ProducerId(connectorId)
		    .SchemaRegistry(schemaRegistry)
		    .Interceptors(interceptors)
		    .Logging(loggingOptions)
		    .Create();

        return new SourceProcessor(connectorId, interceptors, producer, sourceProxy, loggingOptions);
    }

    sealed class SinkConnector(IProcessor processor, SinkProxy sinkProxy) : IConnector {
        public ConnectorId    ConnectorId { get; } = ConnectorId.From(processor.ProcessorId);
        public ConnectorState State       { get; } = (ConnectorState)processor.State;

        public Task Stopped => processor.Stopped;

        public async Task Connect(CancellationToken stoppingToken) {
            await sinkProxy.Initialize(stoppingToken);
            await processor.Activate(stoppingToken);
        }

        public async ValueTask DisposeAsync() {
            await sinkProxy.DisposeAsync();
            await processor.DisposeAsync();
            OnDisposeCallback?.Invoke();
        }
    }

    sealed class SourceConnector(ConnectorId connectorId, IProcessor SourceProcessor) : BackgroundService, IConnector {
        public ConnectorId    ConnectorId { get; } = ConnectorId.From(connectorId);
        public ConnectorState State       => (ConnectorState)SourceProcessor.State;

        public Task Stopped => SourceProcessor.Stopped;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
            await SourceProcessor.Activate(stoppingToken).ConfigureAwait(false);

        public async Task Connect(CancellationToken stoppingToken) =>
            await StartAsync(stoppingToken).ConfigureAwait(false);

        public async ValueTask DisposeAsync() {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            await SourceProcessor.DisposeAsync().ConfigureAwait(false);
            OnDisposeCallback?.Invoke();
        }
    }
}
