// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
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
	private readonly CancellationTokenSource _readLastEventCts;
	private readonly IPublisher _publisher;

	private Task? _readLastEventTask;

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
		_readLastEventCts = new();

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
			} catch (Exception) {
				// ignore
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
		_processor.Tracker.RecordAppended(message.Event, message.CommitPosition);
		return ValueTask.CompletedTask;
	}

	private async Task ReadLastLogEvent() {
		try {
			_readLastEventCts.CancelAfter(TimeSpan.FromSeconds(120));

			var lastLogEvent = await _publisher.Read(Position.End, 1L, false, _readLastEventCts.Token).FirstOrDefaultAsync();

			if (lastLogEvent != default)
				_processor.Tracker.InitLastAppended(ref lastLogEvent);
			else
				Logger.Information("No events found in the log.");
		} catch (Exception exc) {
			Logger.Error(exc, "Error reading last event");
		}
	}
}
