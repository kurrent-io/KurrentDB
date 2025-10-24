// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Threading.Channels;
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

public sealed partial class SecondaryIndexSubscription(
	IPublisher publisher,
	ISecondaryIndexProcessor indexProcessor,
	SecondaryIndexingPluginOptions options,
	ILogger log
) : IAsyncDisposable {
	private readonly int _commitBatchSize = options.CommitBatchSize;
	private CancellationTokenSource? _cts = new();
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;
	private Task? _receivingTask;

	private readonly Channel<ResolvedEvent> _channel = Channel.CreateBounded<ResolvedEvent>(
		new BoundedChannelOptions(options.CommitBatchSize) {
			SingleReader = true,
			SingleWriter = true,
		});

	public void Subscribe() {
		var position = indexProcessor.GetLastPosition();
		var startFrom = position == TFPos.Invalid ? Position.Start : Position.FromInt64(position.CommitPosition, position.PreparePosition);
		log.LogInformation("Using commit batch size {CommitBatchSize}", _commitBatchSize);
		log.LogInformation("Starting indexing subscription from {StartFrom}", startFrom);

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

		_receivingTask = ReceiveRecords(_cts.Token);
		_processingTask = ProcessRecords(_cts.Token);
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	async Task ReceiveRecords(CancellationToken token) {
		if (_subscription == null)
			throw new InvalidOperationException("Subscription not initialized");

		while (!token.IsCancellationRequested) {
			try {
				if (!await _subscription.MoveNextAsync())
					break;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				log.LogWarning("Default indexing subscription is paused because server is not ready");
				await Task.Delay(TimeSpan.FromSeconds(10), token);
				continue;
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

				await _channel.Writer.WriteAsync(resolvedEvent, token);
			} catch (OperationCanceledException) {
				break;
			} catch (Exception e) {
				log.LogError(e, "Error while processing event {EventType}", eventReceived.Event.Event.EventType);
				throw;
			}
		}
		_channel.Writer.Complete();
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task ProcessRecords(CancellationToken token) {
		var indexedCount = 0;
		while (!token.IsCancellationRequested && !_channel.Reader.Completion.IsCompleted) {
			try {
				var resolvedEvent = await _channel.Reader.ReadAsync(token);
				try {

				} catch (Exception e) {
					log.LogError(e, "Error while processing event {EventType}", resolvedEvent.Event.EventType);
					throw;
				}
				indexProcessor.Index(resolvedEvent);
				if (++indexedCount >= _commitBatchSize) {
					indexProcessor.Commit();
					indexedCount = 0;
				}
			} catch (OperationCanceledException) {
				break;
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
		await CompleteTask(_receivingTask);
		await CompleteTask(_processingTask);

		if (_subscription != null) {
			await _subscription.DisposeAsync();
		}

		return;

		async Task CompleteTask(Task? task) {
			if (task == null) {
				return;
			}

			try {
				await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
			} catch (Exception ex) {
				log.LogError(ex, "Error during processing task completion");
			}
		}
	}

	[LoggerMessage(LogLevel.Trace, "Default indexing subscription caught up at {time}")]
	static partial void LogDefaultIndexingSubscriptionCaughtUpAtTime(ILogger logger, DateTime time);
}
