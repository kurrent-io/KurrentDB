// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indexes;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

public class SecondaryIndexSubscription(
	IPublisher publisher,
	ISecondaryIndex index,
	SecondaryIndexingPluginOptions options
) : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexSubscription>();
	private readonly int _commitBatchSize = options.CommitBatchSize;
	private readonly uint _commitDelayMs = options.CommitDelayMs;

	private readonly CancellationTokenSource _cts = new();
	private Enumerator.AllSubscription? _subscription;
	private Task? _processingTask;
	private Committer? _committer;

	public void Subscribe() {
		var position = index.GetLastPosition();
		var startFrom = position == null ? Position.Start : Position.FromInt64((long)position, (long)position);

		_committer = new(_commitBatchSize, _commitDelayMs, () => index.Processor.Commit(), _cts.Token);

		_subscription = new(
			bus: publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			checkpoint: startFrom,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: _cts.Token
		);

		_processingTask = Task.Run(() => ProcessEvents(_cts.Token), _cts.Token);
	}

	private async Task ProcessEvents(CancellationToken token) {
		if (_subscription == null || _committer == null)
			throw new InvalidOperationException("Subscription not initialized");

		while (!token.IsCancellationRequested) {
			if (!await _subscription.MoveNextAsync())
				break;

			if (_subscription.Current is not ReadResponse.EventReceived eventReceived)
				continue;

			try {
				index.Processor.Index(eventReceived.Event);

				_committer.Increment();
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

			if (_committer != null) {
				await _committer.DisposeAsync();
			}

			if (_subscription != null) {
				await _subscription.DisposeAsync();
			}
		} finally {
			_cts.Dispose();
		}
	}
}
