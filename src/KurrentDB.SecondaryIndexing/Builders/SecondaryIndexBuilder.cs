// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.POC.IO.Core;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Builders;

public class SecondaryIndexBuilder :
	IHandle<SystemMessage.SystemReady>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IAsyncHandle<StorageMessage.EventCommitted>,
	IHostedService,
	IAsyncDisposable {
	private static readonly ILogger Logger = Log.Logger.ForContext<SecondaryIndexBuilder>();
	private readonly SecondaryIndexSubscription _subscription;
	private readonly ISecondaryIndexProcessor _processor;
	private readonly ISecondaryIndexProgressTracker _progressTracker;
	private readonly IPublisher _publisher;
	private readonly IClient _client;
	private Task? _readLastEventTask;
	private readonly CancellationTokenSource _readLastEventCts;

	[Experimental("SECONDARY_INDEX")]
	public SecondaryIndexBuilder(
		ISecondaryIndexProcessor processor,
		ISecondaryIndexProgressTracker progressTracker,
		IPublisher publisher,
		ISubscriber subscriber,
		IClient client,
		SecondaryIndexingPluginOptions options
	) {
		_processor = processor;
		_progressTracker = progressTracker;
		_publisher = publisher;
		_client = client;
		_subscription = new SecondaryIndexSubscription(publisher, processor, options);
		_readLastEventCts = new CancellationTokenSource();

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
		subscriber.Subscribe<StorageMessage.EventCommitted>(this);
	}

	public void Handle(SystemMessage.SystemReady message) {
		_readLastEventTask = ReadLastLogEvent();
		_subscription.Subscribe();
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) => _processor.Dispose();

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async ValueTask DisposeAsync() {
		if (_readLastEventTask?.IsCompleted == false) {
			try {
				await _readLastEventCts.CancelAsync();
			} catch (Exception e) {
				Logger.Error(e, "Failed to cancel reading last event");
			}
		}

		try {
			await _subscription.DisposeAsync();
		} catch (Exception e) {
			Logger.Error(e, "Failed to dispose secondary index subscription");
		}

		_processor.Dispose();
	}

	public ValueTask HandleAsync(StorageMessage.EventCommitted message, CancellationToken token) {
		_progressTracker.RecordAppended(message.Event);
		return ValueTask.CompletedTask;
	}

	private async Task ReadLastLogEvent() {
		try {
			_readLastEventCts.CancelAfter(TimeSpan.FromSeconds(120));

			var lastLogEvent = await _client.ReadAllBackwardsFilteredAsync(
					KurrentDB.POC.IO.Core.Position.End,
					1,
					new EventFilter.DefaultAllFilterStrategy.NonSystemStreamStrategy(),
					_readLastEventCts.Token
				)
				.FirstOrDefaultAsync();

			if (lastLogEvent != null)
				_progressTracker.RecordAppended(lastLogEvent);
			else
				Logger.Information("No events found in the log.");
		} catch (Exception exc) {
			Logger.Error(exc, "Error reading last event");
		}
	}
}
