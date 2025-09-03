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

public class DuckDBProjectorService(
	IPublisher publisher,
	ISubscriber subscriber,
	IDuckDBConnectionProvider connectionProvider,
	IConsumerBuilder consumerBuilder,
	ILoggerFactory loggerFactory,
	GetNodeSystemInfo getNodeSystemInfo)
	: LeaderNodeBackgroundService(publisher, subscriber, getNodeSystemInfo, loggerFactory, "DuckDBProjector") {
	private readonly ILoggerFactory _loggerFactory  = loggerFactory;

	protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
		var options = new DuckDBProjectorOptions(connectionProvider) {
			Filter = SchemaRegistryConventions.Filters.SchemasFilter,
			InitialPosition = SubscriptionInitialPosition.Latest,
			AutoCommit = new() {
				Interval = TimeSpan.FromSeconds(5),
				RecordsThreshold = 500
			}
		};
		var projector = new DuckDBProjector(options, new SchemaProjections(), consumerBuilder, _loggerFactory);
		await projector.RunUntilStopped(stoppingToken);
	}
}
