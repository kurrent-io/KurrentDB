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
using KurrentDB.SecondaryIndexing.Indexes.User;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

// The subscription to $all used to populate a particular user index
internal abstract class UserIndexSubscription {
	public abstract ValueTask Start();
	public abstract ValueTask Stop();
	public abstract TFPos GetLastIndexedPosition();
	public abstract void GetUserIndexTableDetails(out string tableName, out string inFlightTableName, out string? fieldName);
}

internal sealed partial class UserIndexSubscription<TField>(
	IPublisher publisher,
	UserIndexProcessor<TField> indexProcessor,
	SecondaryIndexingPluginOptions options,
	Func<EventRecord, bool> eventFilter,
	ILogger log,
	CancellationToken token) : UserIndexSubscription, IAsyncDisposable where TField : IField {
	private readonly int _commitBatchSize = options.CommitBatchSize;
	private CancellationTokenSource? _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;

	private void Subscribe() {
		if (_cts is not { } cts) {
			LogUserIndexSubscriptionAlreadyTerminated(log, indexProcessor.IndexName);
			return;
		}

		var position = indexProcessor.GetLastPosition();
		var startFrom = position == TFPos.Invalid ? Position.Start : Position.FromInt64(position.CommitPosition, position.PreparePosition);
		LogUserIndexSubscriptionIsStarting(log, indexProcessor.IndexName, startFrom);

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

		var indexedCount = 0; // number of events added to the index (must pass the filter and successfully select the field)
		var processedCount = 0; // number of events passed to the processor regardless of whether they were added to the index

		while (!token.IsCancellationRequested) {
			try {
				if (!await _subscription.MoveNextAsync())
					break;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				LogUserIndexIsStoppingBecauseServerIsNotReady(log, indexProcessor.IndexName);
				break;
			}

			if (_subscription.Current is ReadResponse.SubscriptionCaughtUp caughtUp) {
				LogUserIndexCaughtUp(log, indexProcessor.IndexName, caughtUp.Timestamp);
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
						LogUserIndexIsCommitting(log, indexProcessor.IndexName, indexedCount);
						indexProcessor.Commit();
					}

					var lastProcessedPosition = resolvedEvent.OriginalPosition!.Value;
					var lastProcessedTimestamp = resolvedEvent.OriginalEvent.TimeStamp;
					indexProcessor.Checkpoint(lastProcessedPosition, lastProcessedTimestamp);

					indexedCount = 0;
					processedCount = 0;
				}
			} catch (OperationCanceledException) {
				LogUserIndexIsStopping(log, indexProcessor.IndexName);
				break;
			} catch (Exception ex) {
				LogUserIndexFailedToProcessEvent(log, ex, indexProcessor.IndexName, eventReceived.Event.OriginalEventNumber,
					eventReceived.Event.OriginalStreamId, eventReceived.Event.OriginalPosition);
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

	public override ValueTask Start() {
		Subscribe();
		return ValueTask.CompletedTask;
	}

	public override async ValueTask Stop() {
		LogStoppingUserIndexSubscriptionForIndex(log, indexProcessor.IndexName);
		await DisposeAsync();
		indexProcessor.Dispose();
	}

	public override TFPos GetLastIndexedPosition() => indexProcessor.GetLastPosition();

	public override void GetUserIndexTableDetails(out string tableName, out string inFlightTableName, out string? fieldName) =>
		indexProcessor.GetUserIndexTableDetails(out tableName, out inFlightTableName, out fieldName);

	[LoggerMessage(LogLevel.Warning, "User index subscription {index} already terminated")]
	static partial void LogUserIndexSubscriptionAlreadyTerminated(ILogger logger, string index);

	[LoggerMessage(LogLevel.Information, "User index subscription: {index} is starting from {position}")]
	static partial void LogUserIndexSubscriptionIsStarting(ILogger logger, string index, Position position);

	[LoggerMessage(LogLevel.Information, "User index: {index} is stopping because server is not ready")]
	static partial void LogUserIndexIsStoppingBecauseServerIsNotReady(ILogger logger, string index);

	[LoggerMessage(LogLevel.Trace, "User index: {index} caught up at {time}")]
	static partial void LogUserIndexCaughtUp(ILogger logger, string index, DateTime time);

	[LoggerMessage(LogLevel.Trace, "User index: {index} is committing {count} events")]
	static partial void LogUserIndexIsCommitting(ILogger logger, string index, int count);

	[LoggerMessage(LogLevel.Trace, "User index: {index} is stopping as cancellation was requested")]
	static partial void LogUserIndexIsStopping(ILogger logger, string index);

	[LoggerMessage(LogLevel.Error, "User index: {index} failed to process event: {eventNumber}@{streamId} ({position})")]
	static partial void LogUserIndexFailedToProcessEvent(ILogger logger,
		Exception exception,
		string index,
		long eventNumber,
		string streamId,
		TFPos? position);

	[LoggerMessage(LogLevel.Error, "Error during processing task completion")]
	static partial void LogErrorDuringProcessingTaskCompletion(ILogger logger, Exception exception);

	[LoggerMessage(LogLevel.Trace, "Stopping user index subscription for: {index}")]
	static partial void LogStoppingUserIndexSubscriptionForIndex(ILogger logger, string index);
}
