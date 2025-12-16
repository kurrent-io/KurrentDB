// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext.Runtime.CompilerServices;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Readers;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Surge;
using KurrentDB.SecondaryIndexing.Indexes.User;
using KurrentDB.Surge.Readers;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

// The subscription to $all used to populate a particular user index
internal abstract class UserIndexSubscription {
	protected static readonly ILogger Log = Serilog.Log.ForContext<UserIndexSubscription>();

	public abstract ValueTask Start();
	public abstract ValueTask Stop();
	public abstract TFPos GetLastIndexedPosition();
	public abstract void GetUserIndexTableDetails(out string tableName, out string inFlightTableName, out bool hasFields);
}

internal sealed class UserIndexSubscription<TField>(
	ISystemClient client,
	UserIndexProcessor<TField> indexProcessor,
	SecondaryIndexingPluginOptions options,
	ConsumeFilter recordFilter,
	CancellationToken token) : UserIndexSubscription, IAsyncDisposable where TField : IField {

	private readonly SystemReader _reader = new SystemReaderBuilder()
		.Client(client)
		.Create();
	private readonly int _commitBatchSize = options.CommitBatchSize;
	private CancellationTokenSource? _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
	private Task? _processingTask;

	private void Subscribe() {
		if (_cts is not { } cts) {
			Log.Warning("User index subscription {index} already terminated", indexProcessor.IndexName);
			return;
		}
		var position = indexProcessor.GetLastPosition();
		var startFrom = position == TFPos.Invalid ? Position.Start : Position.FromInt64(position.CommitPosition, position.PreparePosition);
		Log.Information("User index subscription: {index} is starting from {position}", indexProcessor.IndexName, startFrom);

		_processingTask = ProcessEvents(startFrom, cts.Token);
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	async Task ProcessEvents(Position startFrom, CancellationToken token) {
		var indexedCount = 0; // number of events added to the index (must pass the filter and successfully select the field)
		var processedCount = 0; // number of events passed to the processor regardless of whether they were added to the index

		LogPosition position = LogPosition.From(startFrom.CommitPosition, startFrom.PreparePosition);
		while (!token.IsCancellationRequested) {
			await foreach (var record in _reader.Read(position, ReadDirection.Forwards, recordFilter, int.MaxValue, token)) {
				try {
					processedCount++;
					if (indexProcessor.TryIndex(record))
						indexedCount++;

					if (processedCount >= _commitBatchSize) {
						if (indexedCount > 0) {
							Log.Verbose("User index: {index} is committing {count} events", indexProcessor.IndexName, indexedCount);
							indexProcessor.Commit();
						}

						var lastProcessedPosition = record.LogPosition.ToTFPos();
						var lastProcessedTimestamp = record.Timestamp;
						indexProcessor.Checkpoint(lastProcessedPosition, lastProcessedTimestamp);

						indexedCount = 0;
						processedCount = 0;
					}
				} catch (OperationCanceledException) {
					Log.Verbose("User index: {index} is stopping as cancellation was requested", indexProcessor.IndexName);
					break;
				} catch (Exception ex) {
					Log.Error(ex, "User index: {index} failed to process event: {eventNumber}@{streamId} ({position})",
						indexProcessor.IndexName, record.Position.StreamRevision.Value, record.Position.StreamId.Value, record.LogPosition);
					throw;
				}
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

		await _reader.DisposeAsync();
	}

	public override ValueTask Start() {
		Subscribe();
		return ValueTask.CompletedTask;
	}

	public override async ValueTask Stop() {
		Log.Verbose("Stopping user index subscription for: {index}", indexProcessor.IndexName);
		await DisposeAsync();
		indexProcessor.Dispose();
	}

	public override TFPos GetLastIndexedPosition() => indexProcessor.GetLastPosition();

	public override void GetUserIndexTableDetails(out string tableName, out string inFlightTableName, out bool hasFields) =>
		indexProcessor.GetUserIndexTableDetails(out tableName, out inFlightTableName, out hasFields);
}
