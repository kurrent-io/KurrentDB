// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.DuckDB.Projectors;
using KurrentDB.Core.Bus;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node;

namespace KurrentDB.SchemaRegistry.Planes.Projection;

public class DuckDBProjectorService : NodeBackgroundService {
	readonly ILoggerFactory _loggerFactory;
	private readonly IDuckDBConnectionProvider _connectionProvider;
	private readonly IConsumerBuilder _consumerBuilder;

	public DuckDBProjectorService(
		IPublisher publisher,
		ISubscriber subscriber,
		IDuckDBConnectionProvider connectionProvider,
		IConsumerBuilder consumerBuilder,
		ILoggerFactory loggerFactory
	) : base(publisher, subscriber, loggerFactory, "DuckDBProjector") {
		_connectionProvider = connectionProvider;
		_consumerBuilder = consumerBuilder;
		_loggerFactory = loggerFactory;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var options = new DuckDBProjectorOptions(_connectionProvider) {
            Filter          = SchemaRegistryConventions.Filters.SchemasFilter,
            InitialPosition = SubscriptionInitialPosition.Latest,
            AutoCommit = new() {
                Interval         = TimeSpan.FromSeconds(5),
                RecordsThreshold = 500
            }
        };

        var projector = new DuckDBProjector(
            options, new SchemaProjections(),
            _consumerBuilder,
            _loggerFactory
        );

        await projector.RunUntilStopped(stoppingToken);
    }
}
