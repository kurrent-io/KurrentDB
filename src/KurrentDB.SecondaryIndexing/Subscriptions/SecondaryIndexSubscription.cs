// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Nodes;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indices;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;


public class SecondaryIndexSubscription(IPublisher publisher, ISecondaryIndex index) {
	static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexSubscription>();
	private const uint CheckpointCommitBatchSize = 50000;
	private const uint CheckpointCommitDelayMs = 10000;
	private const uint DefaultCheckpointIntervalMultiplier = 1000;

	private readonly CancellationTokenSource _cts = new();
	private Enumerator.AllSubscriptionFiltered _sub = null!;
	private Task _runner = null!;

	public async ValueTask Subscribe(CancellationToken cancellationToken) {
		var position = await index.GetLastPosition(cancellationToken);
		var startFrom = position == null ? Position.Start : Position.FromInt64((long)position, (long)position);

		_sub = new Enumerator.AllSubscriptionFiltered(
			bus: publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			checkpoint: startFrom,
			resolveLinks: false,
			eventFilter: EventFilter.DefaultAllFilter,
			user: SystemAccounts.System,
			requiresLeader: false,
			maxSearchWindow: null,
			checkpointIntervalMultiplier: DefaultCheckpointIntervalMultiplier,
			cancellationToken: cancellationToken
		);

		_runner = Task.Run(ProcessEvents, _cts.Token);
	}

	public async ValueTask Unsubscribe(CancellationToken cancellationToken) {
		try {
			await _cts.CancelAsync();
			await _runner;
		} catch {
			//
		} finally {
			await _sub.DisposeAsync();
		}
	}

	private async Task ProcessEvents() {
		while (!_cts.IsCancellationRequested) {
			if (!await _sub.MoveNextAsync()) // not sure if we need to retry forever or if the enumerator will do that for us
				break;

			if (_sub.Current is not ReadResponse.EventReceived eventReceived) continue;

			try {
				// TODO: Add tracing, error handling etc.
				await index.Processor.Index(eventReceived.Event, _cts.Token);
			} catch (TaskCanceledException) {
				// ignore
			} catch (Exception e) {
				Log.Error(e, "Error while processing event {EventType}", eventReceived.Event.Event.EventType);
				throw;
			}
		}
	}
}
