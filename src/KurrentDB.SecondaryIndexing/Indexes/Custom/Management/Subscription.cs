// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Channels;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Polly;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class Subscription : ISecondaryIndexReader {
	private const string ManagementStream = "$secondary-indexes-custom";

	private readonly IPublisher _publisher;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly Meter _meter;
	private readonly IReadIndex<string> _readIndex;
	private readonly Channel<ReadResponse> _channel;
	private readonly ConcurrentDictionary<string, CustomIndexSubscription> _subscriptions = new();
	private readonly CancellationTokenSource _cts;

	private static readonly ILogger Log = Serilog.Log.ForContext<Subscription>();

	public Subscription(
		IPublisher publisher,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		IReadIndex<string> readIndex,
		Meter meter,
		CancellationToken token) {
		_publisher = publisher;
		_options = options;
		_db = db;
		_meter = meter;
		_readIndex = readIndex;

		_cts = CancellationTokenSource.CreateLinkedTokenSource(token);
		_channel = Channel.CreateUnbounded<ReadResponse>();
	}

	public async Task Start() {
		try {
			await StartInternal();
		} catch (OperationCanceledException) {
			Log.Verbose("Subscription to: {stream} is shutting down.", ManagementStream);
		} catch (Exception ex) {
			Log.Fatal(ex, "Subscription to: {stream} has encountered a fatal error.", ManagementStream);
		}
	}

	public async Task Stop() {
		using (_cts)
			await _cts.CancelAsync();

		foreach (var (index, sub) in _subscriptions) {
			try {
				Log.Verbose("Stopping custom index: {index}", index);
				await sub.Stop();
			} catch (Exception ex) {
				Log.Error(ex, "Failed to stop custom index: {index}", index);
			}
		}
	}

	private async Task StartInternal() {
		_cts.Token.ThrowIfCancellationRequested();

		await _publisher.SubscribeToStream(StreamRevision.Start, ManagementStream, _channel, ResiliencePipeline.Empty, _cts.Token);

		Dictionary<string, CreatedEvent> createdIndexes = new();
		HashSet<string> deletedIndexes = new();

		bool caughtUp = false;

		await foreach (var response in _channel.Reader.ReadAllAsync(_cts.Token)) {
			switch (response) {
				case ReadResponse.SubscriptionCaughtUp:
					if (caughtUp)
						continue;

					Log.Verbose("Subscription to: {stream} caught up", ManagementStream);

					caughtUp = true;

					using (_db.Rent(out var connection)) {
						// in some situations, a custom index may have already been marked as deleted in the management stream but not yet in DuckDB:
						// 1) if a node was off or disconnected from the quorum for an extended period of time
						// 2) if a crash occurred mid-deletion
						// so we clean up any remnants at startup
						foreach (var deletedIndex in deletedIndexes) {
							DeleteCustomIndexTable(connection, deletedIndex);
						}
					}

					foreach (var createdIndex in createdIndexes)
						await CreateCustomIndex(createdIndex.Value);

					break;
				case ReadResponse.EventReceived eventReceived:
					var evt = eventReceived.Event;

					Log.Verbose("Subscription to: {stream} received event type: {type}", ManagementStream, evt.OriginalEvent.EventType);

					switch (evt.OriginalEvent.EventType) {
						case "$created":
							var createdEvent = JsonSerializer.Deserialize<CreatedEvent>(evt.Event.Data.Span);

							if (caughtUp)
								await CreateCustomIndex(createdEvent);
							else {
								createdIndexes[createdEvent.Name] = createdEvent;
								deletedIndexes.Remove(createdEvent.Name);
							}

							break;
						case "$deleted":
							var deletedEvent = JsonSerializer.Deserialize<DeletedEvent>(evt.Event.Data.Span);

							if (caughtUp)
								await DeleteCustomIndex(deletedEvent);
							else {
								deletedIndexes.Add(deletedEvent.Name);
								createdIndexes.Remove(deletedEvent.Name);
							}

							break;
						default:
							Log.Warning("Subscription to: {stream} received unknown event type: {type} at event number: {eventNumber}",
								ManagementStream, evt.OriginalEvent.EventType, evt.OriginalEventNumber);
							break;
					}
					break;
			}
		}
	}

	private ValueTask CreateCustomIndex(CreatedEvent createdEvent) {
		return createdEvent.PartitionKeyType switch {
			null => CreateCustomIndex<NullPartitionKey>(createdEvent),
			"number" => CreateCustomIndex<NumberPartitionKey>(createdEvent),
			"string" => CreateCustomIndex<StringPartitionKey>(createdEvent),
			"int16" => CreateCustomIndex<Int16PartitionKey>(createdEvent),
			"int32" => CreateCustomIndex<Int32PartitionKey>(createdEvent),
			"int64" => CreateCustomIndex<Int64PartitionKey>(createdEvent),
			"uint32" => CreateCustomIndex<UInt32PartitionKey>(createdEvent),
			"uint64" => CreateCustomIndex<UInt64PartitionKey>(createdEvent),
			_ => throw new ArgumentOutOfRangeException(nameof(createdEvent.PartitionKeyType))
		};
	}

	private async ValueTask CreateCustomIndex<TPartitionKey>(CreatedEvent createdEvent) where TPartitionKey : ITPartitionKey {
		var indexName = createdEvent.Name;
		Log.Debug("Creating custom index: {index}", indexName);

		var inFlightRecords = new IndexInFlightRecords(_options);

		var processor = new CustomIndexProcessor<TPartitionKey>(
			indexName: createdEvent.Name,
			jsEventFilter: createdEvent.EventFilter,
			jsPartitionKeySelector: createdEvent.PartitionKeySelector,
			db: _db,
			inFlightRecords: inFlightRecords,
			publisher: _publisher,
			meter: _meter);

		var reader = new CustomIndexReader<TPartitionKey>(processor.TableName, _db, inFlightRecords, _readIndex);

		CustomIndexSubscription subscription = new CustomIndexSubscription<TPartitionKey>(
			publisher: _publisher,
			indexProcessor: processor,
			indexReader: reader,
			options: _options,
			token: _cts.Token);

		_subscriptions.TryAdd(createdEvent.Name, subscription);
		await subscription.Start();
	}

	private async Task DeleteCustomIndex(DeletedEvent deletedEvent) {
		var indexName = deletedEvent.Name;
		Log.Debug("Deleting custom index: {index}", indexName);

		var writeLock = AcquireWriteLockForIndex(indexName, out var index);
		using (writeLock) {
			_subscriptions.TryRemove(indexName, out _);
		}

		await index.Stop();
		await index.Delete();
	}

	private static void DeleteCustomIndexTable(DuckDBAdvancedConnection connection, string indexName) {
		var tableName = CustomIndexProcessor.GetTableName(indexName);
		connection.DeleteCustomIndexNonQuery(tableName);
	}

	public bool CanReadIndex(string indexStream) {
		CustomIndex.ParseStreamName(indexStream, out var indexName, out _);
		return _subscriptions.ContainsKey(indexName);
	}

	public TFPos GetLastIndexedPosition(string indexStream) {
		CustomIndex.ParseStreamName(indexStream, out var indexName, out _);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var index))
			return TFPos.Invalid;

		using (readLock)
			return index!.GetLastIndexedPosition();
	}

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		CustomIndex.ParseStreamName(msg.IndexName, out var indexName, out _);
		Log.Verbose("Custom index: {index} received read forwards request", indexName);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var index)) {
			var result = new ClientMessage.ReadIndexEventsForwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			);
			return ValueTask.FromResult(result);
		}

		using (readLock)
			return index!.ReadForwards(msg, token);
	}

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) {
		CustomIndex.ParseStreamName(msg.IndexName, out var indexName, out _);
		Log.Verbose("Custom index: {index} received read backwards request", indexName);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var index)) {
			var result = new ClientMessage.ReadIndexEventsBackwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			);
			return ValueTask.FromResult(result);
		}

		using (readLock)
			return index!.ReadBackwards(msg, token);
	}

	private bool TryAcquireReadLockForIndex(string index, out ReadLock? readLock, out CustomIndexSubscription? subscription) {
		// note: a write lock is acquired only when deleting the index. so, if we cannot acquire a read lock,
		// it means that the custom index is being/has been deleted.

		readLock = null;
		subscription = null;

		if (!_subscriptions.TryGetValue(index, out subscription))
			return false;

		if (!subscription.RWLock.TryEnterReadLock(TimeSpan.Zero))
			return false;

		readLock = new ReadLock(subscription.RWLock);
		return true;
	}

	private WriteLock AcquireWriteLockForIndex(string index, out CustomIndexSubscription subscription) {
		if (!_subscriptions.TryGetValue(index, out var sub))
			throw new Exception($"Failed to acquire write lock for index: {index}");

		sub.RWLock.EnterWriteLock();
		subscription = sub;
		return new WriteLock(sub.RWLock);
	}

	private readonly record struct ReadLock(ReaderWriterLockSlim Lock) : IDisposable {
		public void Dispose() => Lock.ExitReadLock();
	}

	private readonly record struct WriteLock(ReaderWriterLockSlim Lock) : IDisposable {
		public void Dispose() {
			Lock.ExitWriteLock();
			Lock.Dispose(); // the write lock is used only once: when the custom index is deleted
		}
	}
}
