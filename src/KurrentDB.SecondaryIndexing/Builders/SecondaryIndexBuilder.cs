// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Subscriptions;

namespace KurrentDB.SecondaryIndexing.Builders;

public class SecondaryIndexBuilder : IAsyncHandle<SystemMessage.SystemReady>,
	IAsyncHandle<SystemMessage.BecomeShuttingDown> {
	private readonly SecondaryIndexSubscription _subscription;
	private readonly ISecondaryIndex _index;

	[Experimental("SECONDARYINDEXING")]
	public SecondaryIndexBuilder(ISecondaryIndex index, IPublisher publisher) {
		_subscription = new SecondaryIndexSubscription(publisher, index);
		_index = index;
	}

	public async ValueTask HandleAsync(SystemMessage.SystemReady message, CancellationToken token) {
		await _index.Init(token);
		await _subscription.Subscribe(token);
	}

	public async ValueTask HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		await _index.Processor.Commit(token);
		await Task.Delay(100, token);
		_index.Dispose();
	}
}
