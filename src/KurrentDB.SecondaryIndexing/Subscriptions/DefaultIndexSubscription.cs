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
	private bool _rebuilding;

	public void Subscribe() {
		if (_cts is not { } cts) {
			LogAlreadyTerminated(log);
			return;
		}

		var position = indexProcessor.GetLastPosition();
		var startFrom = position == TFPos.Invalid ? Position.Start : Position.FromInt64(position.CommitPosition, position.PreparePosition);
		LogUsingCommitBatchSize(log, _commitBatchSize);
		LogStarting(log, startFrom);
		if (startFrom == Position.Start) {
			log.LogInformation("Rebuilding secondary index from scratch");
			_rebuilding = true;
		}

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
				LogStoppingBecauseServerIsNotReady(log);
				break;
			}

			if (_subscription.Current is ReadResponse.SubscriptionCaughtUp caughtUp) {
				if (_rebuilding) {
					LogIndexRebuildComplete(log, caughtUp.Timestamp);
				} else {
					LogCaughtUpAtTime(log, caughtUp.Timestamp);
				}

				_rebuilding = false;
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
				LogErrorWhileProcessing(log, e, eventReceived.Event.Event.EventType);
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
				LogErrorDuringProcessingTaskCompletion(log, ex);
			}
		}

		if (_subscription != null) {
			await _subscription.DisposeAsync();
		}
	}

	[LoggerMessage(LogLevel.Trace, "Default indexing subscription caught up at {time}")]
	static partial void LogCaughtUpAtTime(ILogger logger, DateTime time);

	[LoggerMessage(LogLevel.Warning, "Subscription already terminated")]
	static partial void LogAlreadyTerminated(ILogger logger);

	[LoggerMessage(LogLevel.Information, "Using commit batch size {commitBatchSize}")]
	static partial void LogUsingCommitBatchSize(ILogger logger, int commitBatchSize);

	[LoggerMessage(LogLevel.Information, "Starting indexing subscription from {startFrom}")]
	static partial void LogStarting(ILogger logger, Position startFrom);

	[LoggerMessage(LogLevel.Information, "Default indexing subscription is stopping because server is not ready")]
	static partial void LogStoppingBecauseServerIsNotReady(ILogger logger);

	[LoggerMessage(LogLevel.Error, "Error while processing event {eventType}")]
	static partial void LogErrorWhileProcessing(ILogger logger, Exception exception, string eventType);

	[LoggerMessage(LogLevel.Error, "Error during processing task completion")]
	static partial void LogErrorDuringProcessingTaskCompletion(ILogger logger, Exception exception);

	[LoggerMessage(LogLevel.Information, "Default secondary indexes rebuild complete at {time}")]
	static partial void LogIndexRebuildComplete(ILogger logger, DateTime time);
}
