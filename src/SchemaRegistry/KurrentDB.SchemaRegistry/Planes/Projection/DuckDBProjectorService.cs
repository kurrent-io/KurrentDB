// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.DuckDB.Projectors;
using Kurrent.Surge.Projectors;
using KurrentDB.Core.Bus;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node.NodeSystemInfo;

namespace KurrentDB.SchemaRegistry.Planes.Projection;

public class DuckDBProjectorService : LeaderNodeBackgroundService {
	public DuckDBProjectorService(
		IPublisher publisher,
		ISubscriber subscriber,
		IDuckDBConnectionProvider connectionProvider,
		IConsumerBuilder consumerBuilder,
		ILoggerFactory loggerFactory,
		GetNodeSystemInfo getNodeSystemInfo
	) : base(publisher, subscriber, getNodeSystemInfo, loggerFactory, "DuckDBProjector") {
		ConnectionProvider = connectionProvider;
		ConsumerBuilder = consumerBuilder;
		LoggerFactory = loggerFactory;
	}

	IDuckDBConnectionProvider ConnectionProvider { get; }
	IConsumerBuilder ConsumerBuilder { get; }
	ILoggerFactory LoggerFactory { get; }

	protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
		var options = new DuckDBProjectorOptions(ConnectionProvider) {
			ConnectionProvider = ConnectionProvider,
			Filter = SchemaRegistryConventions.Filters.SchemasFilter,
			InitialPosition = SubscriptionInitialPosition.Latest,
			AutoCommit = new ProjectorAutoCommitOptions {
				Interval = TimeSpan.FromSeconds(5),
				RecordsThreshold = 500
			}
		};
		var projector = new DuckDBProjector(options, new SchemaProjections(), ConsumerBuilder, LoggerFactory);
		await projector.RunUntilStopped(stoppingToken);
	}
}
