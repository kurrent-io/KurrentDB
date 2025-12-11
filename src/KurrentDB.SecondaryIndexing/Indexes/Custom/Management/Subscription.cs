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
	private readonly ISystemClient _client;
	private readonly IPublisher _publisher;
	private readonly ISchemaSerializer _serializer;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly Meter _meter;
	private readonly Func<(long, DateTime)> _getLastAppendedRecord;
	private readonly IReadIndex<string> _readIndex;
	private readonly Channel<ReadResponse> _channel;
	private readonly ConcurrentDictionary<string, CustomIndexPair> _customIndexes = new();
	private readonly CancellationTokenSource _cts;

	record struct CustomIndexPair(CustomIndexSubscription Subscription, SecondaryIndexReaderBase Reader);

	private static readonly ILogger Log = Serilog.Log.ForContext<Subscription>();

	// ignore system events
	static readonly Func<EventRecord, bool> IgnoreSystemEvents = evt =>
		!evt.EventType.StartsWith('$') &&
		!evt.EventStreamId.StartsWith('$');

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

	public bool CaughtUp { get; private set; }
	public long Checkpoint { get; private set; }

	public async Task Start() {
		try {
			await StartInternal();
		} catch (OperationCanceledException) {
			Log.Verbose("Subscription to: {stream} is shutting down.", CustomIndexConstants.ManagementStream);
		} catch (Exception ex) {
			Log.Fatal(ex, "Subscription to: {stream} has encountered a fatal error.", CustomIndexConstants.ManagementStream);
		}
	}

	public async Task Stop() {
		using (_cts)
			await _cts.CancelAsync();

		foreach (var (index, pair) in _customIndexes) {
			try {
				Log.Verbose("Stopping custom index: {index}", index);
				await pair.Subscription.Stop();
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

		await StartCustomIndex<NullField>(
			indexName: CustomIndexConstants.ManagementIndexName, //qq don't collide with user indices
			createdEvent: new() {
				Filter = "",
				Fields = { },
			},
			eventFilter: evt => evt.EventStreamId.StartsWith($"{CustomIndexConstants.Category}-"));

		await _client.Subscriptions.SubscribeToIndex(
			position: Position.Start,
			indexName: CustomIndexConstants.ManagementStream,
			channel: _channel,
			resiliencePipeline: ResiliencePipelines.RetryForever,
			cancellationToken: _cts.Token);

		Dictionary<string, CustomIndexReadState> customIndexes = [];
		HashSet<string> deletedIndexes = [];

		await foreach (var response in _channel.Reader.ReadAllAsync(_cts.Token)) {
			switch (response) {
				case ReadResponse.SubscriptionCaughtUp:
					if (CaughtUp)
						continue;

					Log.Verbose("Subscription to: {stream} caught up", CustomIndexConstants.ManagementStream);

					CaughtUp = true;

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

				case ReadResponse.CheckpointReceived checkpointReceived:
					var p = checkpointReceived.CommitPosition;
					Checkpoint = p <= long.MaxValue ? (long)p : 0;
					break;

				case ReadResponse.EventReceived eventReceived:
					var evt = eventReceived.Event;

					Log.Verbose("Subscription to: {stream} received event type: {type}", CustomIndexConstants.ManagementStream, evt.OriginalEvent.EventType);

					var deserializedEvent = await _serializer.Deserialize(
						data: evt.OriginalEvent.Data,
						schemaInfo: new(evt.OriginalEvent.EventType, SchemaDataFormat.Json));

					var customIndexName = CustomIndexHelpers.ParseManagementStreamName(evt.OriginalEvent.EventStreamId);

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

							if (CaughtUp)
								await StartCustomIndex(customIndexName, state.Created);

							break;
						}
						case CustomIndexStopped: {
							if (!customIndexes.TryGetValue(customIndexName, out var state))
								break;

							state.Started = false;

							if (CaughtUp)
								await StopCustomIndex(customIndexName);

							break;
						}
						case CustomIndexDeleted: {
							deletedIndexes.Add(customIndexName);
							customIndexes.Remove(customIndexName);

							if (CaughtUp)
								DeleteCustomIndex(customIndexName);

							break;
						}
						default:
							Log.Warning("Subscription to: {stream} received unknown event type: {type} at event number: {eventNumber}",
								CustomIndexConstants.ManagementStream, evt.OriginalEvent.EventType, evt.OriginalEventNumber);
							break;
					}
					break;
			}
		}
	}

	private ValueTask StartCustomIndex(string indexName, CustomIndexCreated createdEvent) {
		if (createdEvent.Fields.Count is 0)
			return StartCustomIndex<NullField>(indexName, createdEvent);

		return createdEvent.Fields[0].Type switch {
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

	private async ValueTask StartCustomIndex<TField>(
		string indexName,
		CustomIndexCreated createdEvent,
		Func<EventRecord, bool>? eventFilter = null) where TField : IField {

		Log.Debug("Starting custom index: {index}", indexName);

		var inFlightRecords = new CustomIndexInFlightRecords<TField>(_options);

		var sql = new CustomIndexSql<TField>(
			indexName,
			createdEvent.Fields.Count is 0
				? ""
				: createdEvent.Fields[0].Name);

		var processor = new CustomIndexProcessor<TField>(
			indexName: indexName,
			jsEventFilter: createdEvent.Filter,
			jsFieldSelector: createdEvent.Fields.Count is 0
				? ""
				: createdEvent.Fields[0].Selector,
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
			options: _options,
			eventFilter: eventFilter ?? IgnoreSystemEvents,
			token: _cts.Token);

		_customIndexes.TryAdd(indexName, new(subscription, reader));
		await subscription.Start();
	}

	private async Task StopCustomIndex(string indexName) {
		Log.Debug("Stopping custom index: {index}", indexName);

		var writeLock = AcquireWriteLockForIndex(indexName, out var index);
		using (writeLock) {
			// we have the write lock, there are no readers. after we release the lock the index is no longer
			// in the dictionary so there can be no new readers.
			_customIndexes.TryRemove(indexName, out _);
		}

		// guaranteed no readers, we can stop.
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
		_publisher.Publish(new StorageMessage.SecondaryIndexDeleted(Custom.CustomIndexHelpers.GetStreamNameRegex(indexName)));
	}

	private static void DeleteCustomIndexTable(DuckDBAdvancedConnection connection, string indexName) {
		CustomIndexSql.DeleteCustomIndex(connection, indexName);
	}

	public bool CanReadIndex(string indexStream) =>
		CustomIndexHelpers.TryParseQueryStreamName(indexStream, out var indexName, out _) &&
		_customIndexes.ContainsKey(indexName);

	public TFPos GetLastIndexedPosition(string indexStream) {
		CustomIndexHelpers.ParseQueryStreamName(indexStream, out var indexName, out _);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var pair))
			return TFPos.Invalid;

		using (readLock)
			return pair.Subscription.GetLastIndexedPosition();
	}

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		CustomIndexHelpers.ParseQueryStreamName(msg.IndexName, out var indexName, out _);
		Log.Verbose("Custom index: {index} received read forwards request", indexName);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var pair)) {
			var result = new ClientMessage.ReadIndexEventsForwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			);
			return ValueTask.FromResult(result);
		}

		using (readLock)
			return pair.Reader.ReadForwards(msg, token);
	}

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) {
		Custom.CustomIndexHelpers.ParseQueryStreamName(msg.IndexName, out var indexName, out _);
		Log.Verbose("Custom index: {index} received read backwards request", indexName);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var pair)) {
			var result = new ClientMessage.ReadIndexEventsBackwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			);
			return ValueTask.FromResult(result);
		}

		using (readLock)
			return pair.Reader.ReadBackwards(msg, token);
	}

	public bool TryGetCustomIndexTableDetails(string indexName, out string tableName, out string inFlightTableName, out bool hasFields) {
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var pair)) {
			tableName = null!;
			inFlightTableName = null!;
			hasFields = false;
			return false;
		}

		using (readLock) {
			pair.Subscription.GetCustomIndexTableDetails(out tableName, out inFlightTableName, out hasFields);
			return true;
		}
	}

	private bool TryAcquireReadLockForIndex(string index, out ReadLock? readLock, out CustomIndexPair pair) {
		// note: a write lock is acquired only when deleting the index. so, if we cannot acquire a read lock,
		// it means that the custom index is being/has been deleted.

		readLock = null;
		pair = default;

		if (!_customIndexes.TryGetValue(index, out pair)) {
			return false;
		}

		if (!pair.Subscription.RWLock.TryEnterReadLock(TimeSpan.Zero))
			return false;

		readLock = new ReadLock(pair.Subscription.RWLock);
		return true;
	}

	private WriteLock AcquireWriteLockForIndex(string index, out CustomIndexSubscription subscription) {
		if (!_customIndexes.TryGetValue(index, out var pair))
			throw new Exception($"Failed to acquire write lock for index: {index}");

		if (!pair.Subscription.RWLock.TryEnterWriteLock(TimeSpan.FromMinutes(1)))
			throw new Exception($"Timed out when acquiring write lock for index: {index}");

		subscription = pair.Subscription;
		return new WriteLock(pair.Subscription.RWLock);
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
