// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public sealed class CustomIndexBuilder :
	IHandle<SystemMessage.SystemReady>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHostedService {

	private readonly IPublisher _publisher;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly Meter _meter;
	private readonly CancellationTokenSource _cts;
	private Subscription? _subscription;

	private static readonly ILogger Log = Serilog.Log.ForContext<CustomIndexBuilder>();

	[Experimental("SECONDARY_INDEX")]
	public CustomIndexBuilder(
		IPublisher publisher,
		ISubscriber subscriber,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)] Meter meter) {
		_publisher = publisher;
		_options = options;
		_db = db;
		_meter = meter;
		_cts = new CancellationTokenSource();

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
	}

	private async Task InitializeManagementStreamSubscription() {
		Log.Verbose("Custom indexes: Initializing subscription to management stream");
		_subscription = new Subscription(_publisher, _options, _db, _meter, _cts.Token);
		await _subscription.Start();
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken) {
		using (_cts)
			await _cts.CancelAsync();

		try {
			if (_subscription is { } sub)
				await sub.Stop();
		} catch (Exception ex) {
			Log.Error(ex, "Custom indexes: Failed to stop subscription to management stream");
		}
	}

	public void Handle(SystemMessage.SystemReady message) {
		Task.Run(InitializeManagementStreamSubscription);
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		Log.Verbose("Custom indexes: Stopping processing as system is shutting down");
		_cts.Cancel();
	}
}
