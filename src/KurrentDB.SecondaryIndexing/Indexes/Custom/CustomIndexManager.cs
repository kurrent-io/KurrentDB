// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public sealed class CustomIndexManager :
	IHandle<SystemMessage.SystemReady>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHostedService,
	ISecondaryIndexReader {

	private readonly ISystemClient _client;
	private readonly IPublisher _publisher;
	private readonly ISchemaSerializer _serializer;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly IReadIndex<string> _index;
	private readonly Meter _meter;

	private Subscription? _subscription;
	private CancellationTokenSource? _cts;

	private static readonly ILogger Log = Serilog.Log.ForContext<CustomIndexManager>();

	[Experimental("SECONDARY_INDEX")]
	public CustomIndexManager(
		ISystemClient client,
		IPublisher publisher,
		ISubscriber subscriber,
		ISchemaSerializer serializer,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		IReadIndex<string> index,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)] Meter meter) {

		_client = client;
		_publisher = publisher;
		_serializer = serializer;
		_options = options;
		_db = db;
		_index = index;
		_meter = meter;
		_cts = new CancellationTokenSource();

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
	}

	private async Task InitializeManagementStreamSubscription() {
		Log.Verbose("Custom indexes: Initializing subscription to management stream");
		_subscription = new Subscription(_client, _publisher, _serializer, _options, _db, _index, _meter, _cts!.Token);
		await _subscription.Start();
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken) {
		if (Interlocked.Exchange(ref _cts, null) is { } cts) {
			using (cts) {
				await cts.CancelAsync();
			}
		}

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
		if (Interlocked.Exchange(ref _cts, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}

	public bool CanReadIndex(string indexStream) => _subscription?.CanReadIndex(indexStream) ?? false;

	public TFPos GetLastIndexedPosition(string indexStream) => _subscription!.GetLastIndexedPosition(indexStream);

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) =>
		_subscription!.ReadForwards(msg, token);

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) =>
		_subscription!.ReadBackwards(msg, token);

	public bool TryGetCustomIndexTableNames(string indexName, out string tableName, out string inFlightTableName) =>
		_subscription!.TryGetCustomIndexTableNames(indexName, out tableName, out inFlightTableName);
}
