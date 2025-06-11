// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using EventStore.Core.Bus;
using EventStore.Core.Duck.Default;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using Serilog;

namespace EventStore.Core.Duck.Infrastructure;

sealed class SecondaryIndexSubscription(
	IPublisher publisher,
	DefaultIndex index
) : IAsyncDisposable {
	static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexSubscription>();

	const int CommitBatchSize = 50_000;
	CancellationTokenSource _cts = new();
	Enumerator.AllSubscription _subscription;
	Task _processingTask;

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
			// liveBufferSize: 200,
			// catchUpBufferSize: options.CommitBatchSize * 2,
			// readBatchSize: 1000,
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

				index.Handler.HandleEvent(resolvedEvent);

				if (++indexedCount >= CommitBatchSize) {
					index.Handler.Commit();
					indexedCount = 0;
				}

				// publisher.Publish(new StorageMessage.SecondaryIndexRecordCommitted(resolvedEvent.Event.LogPosition, resolvedEvent.Event));
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
