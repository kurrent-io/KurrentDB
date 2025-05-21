// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indices;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

public class SecondaryIndexSubscription(
	IPublisher publisher,
	ISecondaryIndex index,
	SecondaryIndexingPluginOptions? options
) : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexSubscription>();
	private readonly int _checkpointCommitBatchSize = options?.CheckpointCommitBatchSize ?? 50000;
	private readonly uint _checkpointCommitDelayMs = options?.CheckpointCommitDelayMs ?? 10000;

	private readonly CancellationTokenSource _cts = new();
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;
	private SecondaryIndexCheckpointTracker? _checkpointTracker;

	public void Subscribe(CancellationToken cancellationToken) {
		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var position = index.GetLastPosition();
		var startFrom = position == null ? Position.Start : Position.FromInt64((long)position, (long)position);

		_checkpointTracker = new SecondaryIndexCheckpointTracker(
			_checkpointCommitBatchSize,
			_checkpointCommitDelayMs,
			ct => index.Processor.Commit(ct),
			linkedCts.Token
		);

		_subscription = new Enumerator.AllSubscription(
			bus: publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			checkpoint: startFrom,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: linkedCts.Token
		);

		_processingTask = Task.Run(() => ProcessEvents(linkedCts.Token), linkedCts.Token);
	}

	private async Task ProcessEvents(CancellationToken token) {
		if (_subscription == null || _checkpointTracker == null)
			throw new InvalidOperationException("Subscription not initialized");

		while (!token.IsCancellationRequested) {
			if (!await _subscription.MoveNextAsync())
				break;

			if (_subscription.Current is not ReadResponse.EventReceived eventReceived)
				continue;

			try {
				await index.Processor.Index(eventReceived.Event, token);

				_checkpointTracker.Increment();
			} catch (OperationCanceledException) {
				break;
			} catch (Exception e) {
				Log.Error(e, "Error while processing event {EventType}", eventReceived.Event.Event.EventType);
				throw;
			}
		}
	}

	public async ValueTask DisposeAsync() {
		try {
			await _cts.CancelAsync();

			if (_processingTask != null) {
				try {
					await _processingTask;
				} catch (OperationCanceledException) {
					// Expected
				} catch (Exception ex) {
					Log.Error(ex, "Error during processing task completion");
				}
			}

			if (_checkpointTracker != null) {
				await _checkpointTracker.DisposeAsync();
			}

			if (_subscription != null) {
				await _subscription.DisposeAsync();
			}
		} finally {
			_cts.Dispose();
		}
	}
}
