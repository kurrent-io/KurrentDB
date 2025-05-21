// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.SecondaryIndexing.Builders;

public class SecondaryIndexBuilder
	: IAsyncHandle<SystemMessage.SystemReady>,
		IAsyncHandle<SystemMessage.BecomeShuttingDown>,
		IHostedService {
	private readonly SecondaryIndexSubscription _subscription;
	private readonly ISecondaryIndex _index;
	public IEnumerable<IVirtualStreamReader> IndexVirtualStreamReaders => _index.Readers;

	[Experimental("SECONDARY_INDEX")]
	public SecondaryIndexBuilder(ISecondaryIndex index, IPublisher publisher, ISubscriber subscriber, SecondaryIndexingPluginOptions? options = null) {
		_subscription = new SecondaryIndexSubscription(publisher, index, options);
		_index = index;

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
	}

	public ValueTask HandleAsync(SystemMessage.SystemReady message, CancellationToken token) {
		_index.Init();
		_subscription.Subscribe(token);

		return ValueTask.CompletedTask;
	}

	public async ValueTask HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		await _index.Processor.Commit(token);
		_index.Dispose();
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
