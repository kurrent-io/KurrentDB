// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.DuckDB.Projectors;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node;

namespace KurrentDB.SchemaRegistry.Planes.Projection;

public class DuckDBProjectorService(
    IPublisher publisher, ISubscriber subscriber, IDuckDBConnectionProvider connectionProvider, IConsumerBuilder consumerBuilder, ILoggerFactory loggerFactory)
    : DuckDBProjectorBackgroundService(publisher, subscriber, loggerFactory.CreateLogger<DuckDBProjectorBackgroundService>(), "DuckDBProjector") {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await WaitForSystemReady(stoppingToken);

        var options = new DuckDBProjectorOptions(connectionProvider) {
            Filter          = SchemaRegistryConventions.Filters.SchemasFilter,
            InitialPosition = SubscriptionInitialPosition.Latest,
            AutoCommit = new() {
                Interval         = TimeSpan.FromSeconds(5),
                RecordsThreshold = 500
            }
        };

        var projector = new DuckDBProjector(
            options, new SchemaProjections(),
            consumerBuilder,
            loggerFactory
        );

        await projector.RunUntilStopped(stoppingToken);
    }
}

// TODO: Refactor to ensure both connector and registry use a unified system readiness component
public class DuckDBProjectorBackgroundService : NodeBackgroundService, IHandle<SystemMessage.SystemReady> {
	readonly TaskCompletionSource _systemReady = new();

	public DuckDBProjectorBackgroundService(IPublisher publisher, ISubscriber subscriber, ILogger<NodeBackgroundService> logger, string? serviceName = null) : base(publisher, logger, serviceName) {
		Logger = logger;
		subscriber.Subscribe(this);
	}

    protected ILogger Logger { get; }

	public void Handle(SystemMessage.SystemReady message) {
		_systemReady.TrySetResult();
	}

	protected async Task WaitForSystemReady(CancellationToken cancellationToken) {
		await _systemReady.Task.WaitAsync(cancellationToken);
		Logger.LogDuckDBProjectorSystemReady(ServiceName);
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken) {
		return Task.CompletedTask;
	}
}

static partial class DuckDBProjectorBackgroundServiceLogMessages {
	[LoggerMessage(LogLevel.Debug, "{ServiceName} system is ready")]
	internal static partial void LogDuckDBProjectorSystemReady(this ILogger logger, string serviceName);
}
