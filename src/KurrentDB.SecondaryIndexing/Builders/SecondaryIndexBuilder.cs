// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Builders;

public class SecondaryIndexBuilder : IHandle<SystemMessage.SystemReady>, IHandle<SystemMessage.BecomeShuttingDown>,
	IHostedService, IAsyncDisposable {
	private static readonly ILogger Logger = Log.Logger.ForContext<SecondaryIndexBuilder>();
	private readonly SecondaryIndexSubscription _subscription;
	private readonly ISecondaryIndexProcessor _processor;

	[Experimental("SECONDARY_INDEX")]
	public SecondaryIndexBuilder(
		ISecondaryIndexProcessor processor,
		IPublisher publisher,
		ISubscriber subscriber,
		SecondaryIndexingPluginOptions options
	) {
		_processor = processor;
		_subscription = new SecondaryIndexSubscription(publisher, processor, options);

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
	}

	public void Handle(SystemMessage.SystemReady message) => _subscription.Subscribe();

	public void Handle(SystemMessage.BecomeShuttingDown message) => _processor.Dispose();

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async ValueTask DisposeAsync() {
		try {
			await _subscription.DisposeAsync();
		} catch (Exception e) {
			Logger.Error(e, "Failed to dispose secondary index subscription");
		}

		_processor.Dispose();
	}
}
