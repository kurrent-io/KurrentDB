// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes;

public sealed class SecondaryIndexBuilder
	: IHandle<SystemMessage.SystemReady>,
		IHandle<SystemMessage.BecomeShuttingDown>,
		IAsyncHandle<StorageMessage.EventCommitted>,
		IHostedService,
		IAsyncDisposable {
	private static readonly ILogger Logger = Log.Logger.ForContext<SecondaryIndexBuilder>();

	private readonly SecondaryIndexSubscription _subscription;
	private readonly ISecondaryIndexProcessor _processor;
	private readonly IPublisher _publisher;

	[Experimental("SECONDARY_INDEX")]
	public SecondaryIndexBuilder(
		ISecondaryIndexProcessor processor,
		IPublisher publisher,
		ISubscriber subscriber,
		SecondaryIndexingPluginOptions options
	) {
		_processor = processor;
		_publisher = publisher;
		_subscription = new(publisher, processor, options);

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
		subscriber.Subscribe<StorageMessage.EventCommitted>(this);
	}

	public void Handle(SystemMessage.SystemReady message) {
		Task.Run(() => InitTracker(default));
		_subscription.Subscribe();
	}

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

	public ValueTask HandleAsync(StorageMessage.EventCommitted message, CancellationToken token) {
		_processor.Tracker.RecordAppended((message.CommitPosition, message.Event.TimeStamp));
		return ValueTask.CompletedTask;
	}

	private async Task InitTracker(CancellationToken cancellationToken) {
		var lastLogEvent = await _publisher.Read(Position.End, 1L, false, cancellationToken).FirstOrDefaultAsync(cancellationToken);

		if (lastLogEvent != default)
			_processor.Tracker.InitLastAppended((lastLogEvent.OriginalPosition!.Value.CommitPosition, lastLogEvent.OriginalEvent.TimeStamp));
		else
			Logger.Information("No events found in the log.");
	}
}
