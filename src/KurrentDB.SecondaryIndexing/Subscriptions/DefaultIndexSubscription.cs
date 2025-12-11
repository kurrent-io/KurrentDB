// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indexes;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

public sealed partial class DefaultIndexSubscription(
	IPublisher publisher,
	ISecondaryIndexProcessor indexProcessor,
	SecondaryIndexingPluginOptions options,
	ILogger log
) : IAsyncDisposable {

	private readonly int _commitBatchSize = options.CommitBatchSize;
	private CancellationTokenSource? _cts = new();
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;

	public void Subscribe() {
		var cts = _cts;
		if (cts == null) {
			log.LogWarning("Subscription already terminated");
			return;
		}
		var position = indexProcessor.GetLastPosition();
		var startFrom = position == TFPos.Invalid ? Position.Start : Position.FromInt64(position.CommitPosition, position.PreparePosition);
		log.LogInformation("Starting indexing subscription from {StartFrom}", startFrom);

		_subscription = new(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: startFrom,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			catchUpBufferSize: options.CommitBatchSize * 2,
			cancellationToken: cts.Token
		);

		_processingTask = ProcessEvents(cts.Token);
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	async Task ProcessEvents(CancellationToken token) {
		if (_subscription == null)
			throw new InvalidOperationException("Subscription not initialized");

		var indexedCount = 0;

		while (!token.IsCancellationRequested) {
			try {
				if (!await _subscription.MoveNextAsync())
					break;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				log.LogInformation("Default indexing subscription is stopping because server is not ready");
				break;
			}

			if (_subscription.Current is ReadResponse.SubscriptionCaughtUp caughtUp) {
				LogDefaultIndexingSubscriptionCaughtUpAtTime(log, caughtUp.Timestamp);
				continue;
			}

			if (_subscription.Current is not ReadResponse.EventReceived eventReceived)
				continue;

			try {
				var resolvedEvent = eventReceived.Event;

				if (resolvedEvent.Event.EventType.StartsWith('$') || resolvedEvent.Event.EventStreamId.StartsWith('$')) {
					// ignore system events
					continue;
				}

				indexProcessor.TryIndex(resolvedEvent);

				if (++indexedCount >= _commitBatchSize) {
					indexProcessor.Commit();
					indexedCount = 0;
				}
			} catch (OperationCanceledException) {
				break;
			} catch (Exception e) {
				log.LogError(e, "Error while processing event {EventType}", eventReceived.Event.Event.EventType);
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
				log.LogError(ex, "Error during processing task completion");
			}
		}

		if (_subscription != null) {
			await _subscription.DisposeAsync();
		}
	}

	[LoggerMessage(LogLevel.Trace, "Default indexing subscription caught up at {time}")]
	static partial void LogDefaultIndexingSubscriptionCaughtUpAtTime(ILogger logger, DateTime time);
}
