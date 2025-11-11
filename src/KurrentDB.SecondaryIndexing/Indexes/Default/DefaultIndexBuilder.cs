// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

public sealed class DefaultIndexBuilder
	: IHandle<SystemMessage.SystemReady>,
		IHandle<SystemMessage.BecomeShuttingDown>,
		IHandle<StorageMessage.EventCommitted>,
		IHostedService,
		IAsyncDisposable {
	private readonly DefaultIndexSubscription _subscription;
	private readonly ISecondaryIndexProcessor _processor;
	private readonly ILogger<DefaultIndexBuilder> _log;
	private readonly IPublisher _publisher;

	[Experimental("SECONDARY_INDEX")]
	public DefaultIndexBuilder(
		ISecondaryIndexProcessor processor,
		IPublisher publisher,
		ISubscriber subscriber,
		SecondaryIndexingPluginOptions options,
		ILogger<DefaultIndexBuilder> log) {
		_processor = processor;
		_subscription = new(publisher, processor, options, log);
		_log = log;
		_publisher = publisher;

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
		subscriber.Subscribe<StorageMessage.EventCommitted>(this);
	}

	public void Handle(SystemMessage.SystemReady message) {
		_subscription.Subscribe();
		Task.Run(ReadTail);
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) => _processor.Dispose();

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async ValueTask DisposeAsync() {
		try {
			await _subscription.DisposeAsync();
		} catch (Exception e) {
			_log.LogError(e, "Failed to dispose secondary index subscription");
		}

		_processor.Dispose();
	}

	private async Task ReadTail() {
		var lastRecord = await _publisher.ReadBackwards(Position.End, new Filter(), 1).FirstOrDefaultAsync();
		if (lastRecord != default) {
			_processor.Tracker.RecordAppended(lastRecord.Event, lastRecord.OriginalPosition!.Value.CommitPosition);
		}
	}

	public void Handle(StorageMessage.EventCommitted message) {
		if (!message.Event.EventStreamId.StartsWith('$')) {
			_processor.Tracker.RecordAppended(message.Event, message.CommitPosition);
		}
	}

	private class Filter : IEventFilter {
		public bool IsEventAllowed(EventRecord eventRecord) => !eventRecord.EventStreamId.StartsWith('$');
	}
}
