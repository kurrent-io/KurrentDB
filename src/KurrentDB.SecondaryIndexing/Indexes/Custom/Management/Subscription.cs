// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Channels;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Resilience;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class Subscription : ISecondaryIndexReader {
	private const string ManagementStream = CustomIndexConstants.ManagementStream;
	private readonly ISystemClient _client;
	private readonly IPublisher _publisher;
	private readonly ISchemaSerializer _serializer;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly Meter _meter;
	private readonly Func<(long, DateTime)> _getLastAppendedRecord;
	private readonly IReadIndex<string> _readIndex;
	private readonly Channel<ReadResponse> _channel;
	private readonly ConcurrentDictionary<string, CustomIndexSubscription> _subscriptions = new();
	private readonly CancellationTokenSource _cts;

	private static readonly ILogger Log = Serilog.Log.ForContext<Subscription>();

	public Subscription(
		ISystemClient client,
		IPublisher publisher,
		ISchemaSerializer serializer,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		IReadIndex<string> readIndex,
		Meter meter,
		Func<(long, DateTime)> getLastAppendedRecord,
		CancellationToken token) {

		_client = client;
		_publisher = publisher;
		_serializer = serializer;
		_options = options;
		_db = db;
		_meter = meter;
		_getLastAppendedRecord = getLastAppendedRecord;
		_readIndex = readIndex;

		_cts = CancellationTokenSource.CreateLinkedTokenSource(token);
		_channel = Channel.CreateBounded<ReadResponse>(new BoundedChannelOptions(capacity: 20));
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

	class CustomIndexReadState {
		public CustomIndexCreated Created { get; set; } = null!;
		public bool Started { get; set; }
	}

	private async Task StartInternal() {
		_cts.Token.ThrowIfCancellationRequested();

		await _client.Subscriptions.SubscribeToIndex(
			position: Position.Start,
			indexName: ManagementStream,
			channel: _channel,
			resiliencePipeline: ResiliencePipelines.RetryForever,
			cancellationToken: _cts.Token);

		Dictionary<string, CustomIndexReadState> customIndexes = [];
		HashSet<string> deletedIndexes = [];

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

					foreach (var (name, state) in customIndexes) {
						if (state.Started) {
							await StartCustomIndex(name, state.Created);
						}
					}

					break;
				case ReadResponse.EventReceived eventReceived:
					var evt = eventReceived.Event;

					Log.Verbose("Subscription to: {stream} received event type: {type}", ManagementStream, evt.OriginalEvent.EventType);

					var deserializedEvent = await _serializer.Deserialize(
						data: evt.OriginalEvent.Data,
						schemaInfo: new(evt.OriginalEvent.EventType, SchemaDataFormat.Json));

					//qq refactor, put place that writes and reads stream names together.
					var streamName = evt.OriginalEvent.EventStreamId;
					var customIndexName = streamName[(streamName.IndexOf('-') + 1) ..];

					switch (deserializedEvent) {
						case CustomIndexCreated createdEvent: {
							deletedIndexes.Remove(customIndexName);
							customIndexes[customIndexName] = new() {
								Created = createdEvent,
								Started = false,
							};
							break;
						}
						case CustomIndexStarted: {
							if (!customIndexes.TryGetValue(customIndexName, out var state))
								break;

							state.Started = true;

							if (caughtUp)
								await StartCustomIndex(customIndexName, state.Created);

							break;
						}
						case CustomIndexStopped: {
							if (!customIndexes.TryGetValue(customIndexName, out var state))
								break;

							state.Started = false;

							if (caughtUp)
								await StopCustomIndex(customIndexName);

							break;
						}
						case CustomIndexDeleted: {
							deletedIndexes.Add(customIndexName);
							customIndexes.Remove(customIndexName);

							if (caughtUp)
								DeleteCustomIndex(customIndexName);

							break;
						}
						default:
							Log.Warning("Subscription to: {stream} received unknown event type: {type} at event number: {eventNumber}",
								ManagementStream, evt.OriginalEvent.EventType, evt.OriginalEventNumber);
							break;
					}
					break;
			}
		}
	}

	private ValueTask StartCustomIndex(string indexName, CustomIndexCreated createdEvent) {
		return createdEvent.Fields[0].Type switch {
			FieldType.Unspecified => StartCustomIndex<NullField>(indexName, createdEvent),
			FieldType.Double => StartCustomIndex<DoubleField>(indexName, createdEvent),
			FieldType.String => StartCustomIndex<StringField>(indexName, createdEvent),
			FieldType.Int16 => StartCustomIndex<Int16Field>(indexName, createdEvent),
			FieldType.Int32 => StartCustomIndex<Int32Field>(indexName, createdEvent),
			FieldType.Int64 => StartCustomIndex<Int64Field>(indexName, createdEvent),
			FieldType.Uint32 => StartCustomIndex<UInt32Field>(indexName, createdEvent),
			FieldType.Uint64 => StartCustomIndex<UInt64Field>(indexName, createdEvent),
			_ => throw new ArgumentOutOfRangeException("Field type")
		};
	}

	private async ValueTask StartCustomIndex<TField>(string indexName, CustomIndexCreated createdEvent) where TField : IField {
		Log.Debug("Starting custom index: {index}", indexName);

		var inFlightRecords = new IndexInFlightRecords(_options);

		var sql = new CustomIndexSql<TField>(indexName);

		var processor = new CustomIndexProcessor<TField>(
			indexName: indexName,
			jsEventFilter: createdEvent.Filter,
			jsFieldSelector: createdEvent.Fields[0].Selector,
			db: _db,
			sql: sql,
			inFlightRecords: inFlightRecords,
			publisher: _publisher,
			meter: _meter,
			getLastAppendedRecord: _getLastAppendedRecord);

		var reader = new CustomIndexReader<TField>(sharedPool: _db, sql, inFlightRecords, _readIndex);

		CustomIndexSubscription subscription = new CustomIndexSubscription<TField>(
			publisher: _publisher,
			indexProcessor: processor,
			indexReader: reader,
			options: _options,
			token: _cts.Token);

		_subscriptions.TryAdd(indexName, subscription);
		await subscription.Start();
	}

	private async Task StopCustomIndex(string indexName) {
		Log.Debug("Stopping custom index: {index}", indexName);

		var writeLock = AcquireWriteLockForIndex(indexName, out var index);
		using (writeLock) {
			_subscriptions.TryRemove(indexName, out _);
		}

		await index.Stop();
	}

	private void DeleteCustomIndex(string indexName) {
		Log.Debug("Deleting custom index: {index}", indexName);

		using (_db.Rent(out var connection)) {
			DeleteCustomIndexTable(connection, indexName);
		}

		DropSubscriptions(indexName);
	}

	private void DropSubscriptions(string indexName) {
		Log.Verbose("Dropping subscriptions to custom index: {index}", indexName);
		_publisher.Publish(new StorageMessage.SecondaryIndexDeleted(Custom.CustomIndex.GetStreamNameRegex(indexName)));
	}

	private static void DeleteCustomIndexTable(DuckDBAdvancedConnection connection, string indexName) {
		CustomIndexSql.DeleteCustomIndex(connection, indexName);
	}

	public bool CanReadIndex(string indexStream) {
		Custom.CustomIndex.ParseStreamName(indexStream, out var indexName, out _);
		return _subscriptions.ContainsKey(indexName);
	}

	public TFPos GetLastIndexedPosition(string indexStream) {
		Custom.CustomIndex.ParseStreamName(indexStream, out var indexName, out _);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var index))
			return TFPos.Invalid;

		using (readLock)
			return index!.GetLastIndexedPosition();
	}

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		Custom.CustomIndex.ParseStreamName(msg.IndexName, out var indexName, out _);
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
		Custom.CustomIndex.ParseStreamName(msg.IndexName, out var indexName, out _);
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

	public bool TryGetCustomIndexTableDetails(string indexName, out string tableName, out string inFlightTableName, out bool hasPartitions) {
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var index)) {
			tableName = null!;
			inFlightTableName = null!;
			hasPartitions = false;
			return false;
		}

		using (readLock) {
			index!.GetCustomIndexTableDetails(out tableName, out inFlightTableName, out hasPartitions);
			return true;
		}
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

		if (!sub.RWLock.TryEnterWriteLock(TimeSpan.FromMinutes(1)))
			throw new Exception($"Timed out when acquiring write lock for index: {index}");

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
