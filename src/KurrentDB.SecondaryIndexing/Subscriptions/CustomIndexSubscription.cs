// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indexes.Custom;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

// The subscription to $all used to populate a particular custom index
internal abstract class CustomIndexSubscription {
	protected static readonly ILogger Log = Serilog.Log.ForContext<CustomIndexSubscription>();

	public abstract ValueTask Start();
	public abstract ValueTask Stop();
	public abstract TFPos GetLastIndexedPosition();
	public abstract void GetCustomIndexTableDetails(out string tableName, out string inFlightTableName, out bool hasFields);
	public abstract ReaderWriterLockSlim RWLock { get; }
}

internal sealed class CustomIndexSubscription<TField>(
	IPublisher publisher,
	CustomIndexProcessor<TField> indexProcessor,
	SecondaryIndexingPluginOptions options,
	Func<EventRecord, bool> eventFilter,
	CancellationToken token) : CustomIndexSubscription, IAsyncDisposable where TField : IField {

	private readonly int _commitBatchSize = options.CommitBatchSize;
	private CancellationTokenSource? _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;

	private void Subscribe() {
		var position = indexProcessor.GetLastPosition();
		var startFrom = position == TFPos.Invalid ? Position.Start : Position.FromInt64(position.CommitPosition, position.PreparePosition);
		Log.Information("Custom index subscription: {index} is starting from {position}", indexProcessor.IndexName, startFrom);

		_subscription = new(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: startFrom,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			catchUpBufferSize: options.CommitBatchSize * 2,
			cancellationToken: _cts!.Token
		);

		_processingTask = ProcessEvents(_cts.Token);
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	async Task ProcessEvents(CancellationToken token) {
		if (_subscription == null)
			throw new InvalidOperationException("Subscription not initialized");

		var indexedCount = 0;
		var processedCount = 0;

		while (!token.IsCancellationRequested) {
			try {
				if (!await _subscription.MoveNextAsync())
					break;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				Log.Information("Custom index: {index} is stopping because server is not ready", indexProcessor.IndexName);
				break;
			}

			if (_subscription.Current is ReadResponse.SubscriptionCaughtUp caughtUp) {
				Log.Verbose("Custom index: {index} caught up at {time}", indexProcessor.IndexName, caughtUp.Timestamp);
				continue;
			}

			if (_subscription.Current is not ReadResponse.EventReceived eventReceived)
				continue;

			try {
				var resolvedEvent = eventReceived.Event;

				if (!eventFilter(resolvedEvent.Event)) {
					continue;
				}

				processedCount++;
				if (indexProcessor.TryIndex(resolvedEvent))
					indexedCount++;

				if (processedCount >= _commitBatchSize) {
					if (indexedCount > 0) {
						Log.Verbose("Custom index: {index} is committing {count} events", indexProcessor.IndexName, indexedCount);
						indexProcessor.Commit();
					}

					var lastProcessedPosition = resolvedEvent.OriginalPosition!.Value;
					var lastProcessedTimestamp = resolvedEvent.OriginalEvent.TimeStamp;
					indexProcessor.Checkpoint(lastProcessedPosition, lastProcessedTimestamp);

					indexedCount = 0;
					processedCount = 0;
				}
			} catch (OperationCanceledException) {
				Log.Verbose("Custom index: {index} is stopping as cancellation was requested", indexProcessor.IndexName);
				break;
			} catch (Exception ex) {
				Log.Error(ex, "Custom index: {index} failed to process event: {eventNumber}@{streamId} ({position})",
					indexProcessor.IndexName, eventReceived.Event.OriginalEventNumber, eventReceived.Event.OriginalStreamId, eventReceived.Event.OriginalPosition);
				throw;
			}
		}
	}

	public ValueTask DisposeAsync() {
		// dispose CTS once to deal with the concurrent call to the current method
		if (Interlocked.Exchange(ref _cts, null) is not { } cts)
			return ValueTask.CompletedTask;

		using (cts) {
			cts.Cancel();
		}

		return DisposeCoreAsync();
	}

	private async ValueTask DisposeCoreAsync() {
		if (_processingTask != null) {
			try {
				await _processingTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing |
													 ConfigureAwaitOptions.ContinueOnCapturedContext);
			} catch (Exception ex) {
				Log.Error(ex, "Error during processing task completion");
			}
		}

		if (_subscription != null) {
			await _subscription.DisposeAsync();
		}
	}

	public override ValueTask Start() {
		Subscribe();
		return ValueTask.CompletedTask;
	}

	public override async ValueTask Stop() {
		Log.Verbose("Stopping custom index subscription for: {index}", indexProcessor.IndexName);
		await DisposeAsync();
		indexProcessor.Dispose();
	}

	public override TFPos GetLastIndexedPosition() => indexProcessor.GetLastPosition();

	public override void GetCustomIndexTableDetails(out string tableName, out string inFlightTableName, out bool hasFields) =>
		indexProcessor.GetCustomIndexTableDetails(out tableName, out inFlightTableName, out hasFields);

	public override ReaderWriterLockSlim RWLock { get; } = new();
}
