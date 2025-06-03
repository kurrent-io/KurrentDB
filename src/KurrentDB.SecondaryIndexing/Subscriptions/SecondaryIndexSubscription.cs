// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indexes;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

public sealed class SecondaryIndexSubscription(
	IPublisher publisher,
	ISecondaryIndex index,
	SecondaryIndexingPluginOptions options
) : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexSubscription>();

	private readonly int _commitBatchSize = options.CommitBatchSize;
	private CancellationTokenSource? _cts = new();
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;

	public void Subscribe() {
		var position = index.GetLastPosition();
		var startFrom = position == null ? Position.Start : Position.FromInt64((long)position, (long)position);

		_subscription = new(
			bus: publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			checkpoint: startFrom,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			maxLiveBufferCount: 5000,
			readBatchSize: 5000,
			cancellationToken: _cts!.Token
		);

		_processingTask = ProcessEvents(_cts.Token);
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task ProcessEvents(CancellationToken token) {
		if (_subscription == null)
			throw new InvalidOperationException("Subscription not initialized");

		var indexedCount = 0;

		while (!token.IsCancellationRequested) {
			if (!await _subscription.MoveNextAsync())
				break;

			if (_subscription.Current is not ReadResponse.EventReceived eventReceived)
				continue;

			try {
				var resolvedEvent = eventReceived.Event;

				if (resolvedEvent.Event.EventType.StartsWith('$') || resolvedEvent.Event.EventStreamId.StartsWith('$')) {
					// ignore system events
					continue;
				}

				index.Index(resolvedEvent);

				if (++indexedCount >= _commitBatchSize) {
					index.Commit();
					indexedCount = 0;
				}

				publisher.Publish(new StorageMessage.SecondaryIndexRecordCommitted(resolvedEvent.Event.LogPosition, resolvedEvent.Event));
			} catch (OperationCanceledException) {
				break;
			} catch (Exception e) {
				Log.Error(e, "Error while processing event {EventType}", eventReceived.Event.Event.EventType);
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

	async ValueTask DisposeCoreAsync() {
		if (_processingTask != null) {
			try {
				await _processingTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing |
				                               ConfigureAwaitOptions.ContinueOnCapturedContext);
			} catch (Exception ex) {
				Log.Error(ex, "Error during processing task completion");
			}
		}

		index.Dispose();

		if (_subscription != null) {
			await _subscription.DisposeAsync();
		}
	}
}
