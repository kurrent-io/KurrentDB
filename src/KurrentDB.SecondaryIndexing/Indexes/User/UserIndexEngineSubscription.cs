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
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public class UserIndexEngineSubscription : ISecondaryIndexReader {
	private readonly ISystemClient _client;
	private readonly IPublisher _publisher;
	private readonly ISchemaSerializer _serializer;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly Meter _meter;
	private readonly Func<(long, DateTime)> _getLastAppendedRecord;
	private readonly IReadIndex<string> _readIndex;
	private readonly Channel<ReadResponse> _channel;
	private readonly ConcurrentDictionary<string, UserIndexData> _userIndexes = new();
	private readonly CancellationTokenSource _cts;

	record struct UserIndexData(
		ReaderWriterLockSlim RWLock,
		UserIndexSubscription Subscription,
		SecondaryIndexReaderBase Reader);

	private static readonly ILogger Log = Serilog.Log.ForContext<UserIndexEngineSubscription>();

	// ignore system events
	static readonly Func<EventRecord, bool> IgnoreSystemEvents = evt =>
		!evt.EventType.StartsWith('$') &&
		!evt.EventStreamId.StartsWith('$');

	public UserIndexEngineSubscription(
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
			Log.Verbose("Subscription to: {stream} is shutting down.", UserIndexConstants.ManagementStream);
		} catch (Exception ex) {
			Log.Fatal(ex, "Subscription to: {stream} has encountered a fatal error.", UserIndexConstants.ManagementStream);
		}
	}

	public async Task Stop() {
		using (_cts)
			await _cts.CancelAsync();

		foreach (var (index, data) in _userIndexes) {
			try {
				Log.Verbose("Stopping user index: {index}", index);
				await data.Subscription.Stop();
			} catch (Exception ex) {
				Log.Error(ex, "Failed to stop user index: {index}", index);
			}
		}
	}

	class UserIndexReadState {
		public IndexCreated Created { get; set; } = null!;
		public bool Started { get; set; }
	}

	private async Task StartInternal() {
		_cts.Token.ThrowIfCancellationRequested();

		// todo: don't collide with user indices, or use a default index if we add one with system events
		await StartUserIndex<NullField>(
			indexName: UserIndexConstants.ManagementIndexName,
			createdEvent: new() {
				Filter = "",
				Fields = { },
			},
			eventFilter: evt => evt.EventStreamId.StartsWith($"{UserIndexConstants.Category}-"));

		await _client.Subscriptions.SubscribeToIndex(
			position: Position.Start,
			indexName: UserIndexConstants.ManagementStream,
			channel: _channel,
			resiliencePipeline: ResiliencePipelines.RetryForever,
			cancellationToken: _cts.Token);

		Dictionary<string, UserIndexReadState> userIndexes = [];
		HashSet<string> deletedIndexes = [];

		await foreach (var response in _channel.Reader.ReadAllAsync(_cts.Token)) {
			switch (response) {
				case ReadResponse.SubscriptionCaughtUp:
					if (CaughtUp)
						continue;

					Log.Verbose("Subscription to: {stream} caught up", UserIndexConstants.ManagementStream);

					CaughtUp = true;

					using (_db.Rent(out var connection)) {
						// in some situations, a user index may have already been marked as deleted in the management stream but not yet in DuckDB:
						// 1) if a node was off or disconnected from the quorum for an extended period of time
						// 2) if a crash occurred mid-deletion
						// so we clean up any remnants at startup
						foreach (var deletedIndex in deletedIndexes) {
							DeleteUserIndexTable(connection, deletedIndex);
						}
					}

					foreach (var (name, state) in userIndexes) {
						if (state.Started) {
							await StartUserIndex(name, state.Created);
						}
					}

					break;

				case ReadResponse.CheckpointReceived checkpointReceived:
					var p = checkpointReceived.CommitPosition;
					Checkpoint = p <= long.MaxValue ? (long)p : 0;
					break;

				case ReadResponse.EventReceived eventReceived:
					var evt = eventReceived.Event;

					Log.Verbose("Subscription to: {stream} received event type: {type}", UserIndexConstants.ManagementStream, evt.OriginalEvent.EventType);

					var deserializedEvent = await _serializer.Deserialize(
						data: evt.OriginalEvent.Data,
						schemaInfo: new(evt.OriginalEvent.EventType, SchemaDataFormat.Json));

					var userIndexName = UserIndexHelpers.ParseManagementStreamName(evt.OriginalEvent.EventStreamId);

					switch (deserializedEvent) {
						case IndexCreated createdEvent: {
							deletedIndexes.Remove(userIndexName);
							userIndexes[userIndexName] = new() {
								Created = createdEvent,
								Started = false,
							};
							break;
						}
						case IndexStarted: {
							if (!userIndexes.TryGetValue(userIndexName, out var state))
								break;

							state.Started = true;

							if (CaughtUp)
								await StartUserIndex(userIndexName, state.Created);

							break;
						}
						case IndexStopped: {
							if (!userIndexes.TryGetValue(userIndexName, out var state))
								break;

							state.Started = false;

							if (CaughtUp)
								await StopUserIndex(userIndexName);

							break;
						}
						case IndexDeleted: {
							deletedIndexes.Add(userIndexName);
							userIndexes.Remove(userIndexName);

							if (CaughtUp)
								DeleteUserIndex(userIndexName);

							break;
						}
						default:
							Log.Warning("Subscription to: {stream} received unknown event type: {type} at event number: {eventNumber}",
								UserIndexConstants.ManagementStream, evt.OriginalEvent.EventType, evt.OriginalEventNumber);
							break;
					}
					break;
			}
		}
	}

	private ValueTask StartUserIndex(string indexName, IndexCreated createdEvent) {
		if (createdEvent.Fields.Count is 0)
			return StartUserIndex<NullField>(indexName, createdEvent);

		return createdEvent.Fields[0].Type switch {
			IndexFieldType.Double => StartUserIndex<DoubleField>(indexName, createdEvent),
			IndexFieldType.String => StartUserIndex<StringField>(indexName, createdEvent),
//			IndexFieldType.Int16 => StartUserIndex<Int16Field>(indexName, createdEvent),
			IndexFieldType.Int32 => StartUserIndex<Int32Field>(indexName, createdEvent),
			IndexFieldType.Int64 => StartUserIndex<Int64Field>(indexName, createdEvent),
//			IndexFieldType.Uint32 => StartUserIndex<UInt32Field>(indexName, createdEvent),
//			IndexFieldType.Uint64 => StartUserIndex<UInt64Field>(indexName, createdEvent),
			_ => throw new ArgumentOutOfRangeException("Field type")
		};
	}

	private async ValueTask StartUserIndex<TField>(
		string indexName,
		IndexCreated createdEvent,
		Func<EventRecord, bool>? eventFilter = null) where TField : IField {

		Log.Debug("Starting user index: {index}", indexName);

		var inFlightRecords = new UserIndexInFlightRecords<TField>(_options);

		var sql = new UserIndexSql<TField>(
			indexName,
			createdEvent.Fields.Count is 0
				? ""
				: createdEvent.Fields[0].Name);

		var processor = new UserIndexProcessor<TField>(
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

		var reader = new UserIndexReader<TField>(sharedPool: _db, sql, inFlightRecords, _readIndex);

		UserIndexSubscription subscription = new UserIndexSubscription<TField>(
			publisher: _publisher,
			indexProcessor: processor,
			options: _options,
			eventFilter: eventFilter ?? IgnoreSystemEvents,
			token: _cts.Token);

		_userIndexes.TryAdd(indexName, new(new ReaderWriterLockSlim(), subscription, reader));
		await subscription.Start();
	}

	private async Task StopUserIndex(string indexName) {
		Log.Debug("Stopping user index: {index}", indexName);

		var writeLock = AcquireWriteLockForIndex(indexName, out var index);
		using (writeLock) {
			// we have the write lock, there are no readers. after we release the lock the index is no longer
			// in the dictionary so there can be no new readers.
			_userIndexes.TryRemove(indexName, out _);
		}

		// guaranteed no readers, we can stop.
		await index.Stop();
	}

	private void DeleteUserIndex(string indexName) {
		Log.Debug("Deleting user index: {index}", indexName);

		using (_db.Rent(out var connection)) {
			DeleteUserIndexTable(connection, indexName);
		}

		DropSubscriptions(indexName);
	}

	private void DropSubscriptions(string indexName) {
		Log.Verbose("Dropping subscriptions to user index: {index}", indexName);
		_publisher.Publish(new StorageMessage.SecondaryIndexDeleted(UserIndexHelpers.GetStreamNameRegex(indexName)));
	}

	private static void DeleteUserIndexTable(DuckDBAdvancedConnection connection, string indexName) {
		UserIndexSql.DeleteUserIndex(connection, indexName);
	}

	public bool CanReadIndex(string indexStream) =>
		UserIndexHelpers.TryParseQueryStreamName(indexStream, out var indexName, out _) &&
		_userIndexes.ContainsKey(indexName);

	public TFPos GetLastIndexedPosition(string indexStream) {
		UserIndexHelpers.ParseQueryStreamName(indexStream, out var indexName, out _);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var data))
			return TFPos.Invalid;

		using (readLock)
			return data.Subscription.GetLastIndexedPosition();
	}

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		UserIndexHelpers.ParseQueryStreamName(msg.IndexName, out var indexName, out _);
		Log.Verbose("User index: {index} received read forwards request", indexName);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var data)) {
			var result = new ClientMessage.ReadIndexEventsForwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			);
			return ValueTask.FromResult(result);
		}

		using (readLock)
			return data.Reader.ReadForwards(msg, token);
	}

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) {
		UserIndexHelpers.ParseQueryStreamName(msg.IndexName, out var indexName, out _);
		Log.Verbose("User index: {index} received read backwards request", indexName);
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var data)) {
			var result = new ClientMessage.ReadIndexEventsBackwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			);
			return ValueTask.FromResult(result);
		}

		using (readLock)
			return data.Reader.ReadBackwards(msg, token);
	}

	public bool TryGetUserIndexTableDetails(string indexName, out string tableName, out string inFlightTableName, out string? fieldName) {
		if (!TryAcquireReadLockForIndex(indexName, out var readLock, out var data)) {
			tableName = null!;
			inFlightTableName = null!;
			fieldName = null;
			return false;
		}

		using (readLock) {
			data.Subscription.GetUserIndexTableDetails(out tableName, out inFlightTableName, out fieldName);
			return true;
		}
	}

	private bool TryAcquireReadLockForIndex(string index, out ReadLock? readLock, out UserIndexData data) {
		// note: a write lock is acquired only when deleting the index. so, if we cannot acquire a read lock,
		// it means that the user index is being/has been deleted.

		readLock = null;
		data = default;

		if (!_userIndexes.TryGetValue(index, out data)) {
			return false;
		}

		if (!data.RWLock.TryEnterReadLock(TimeSpan.Zero))
			return false;

		readLock = new ReadLock(data.RWLock);
		return true;
	}

	private WriteLock AcquireWriteLockForIndex(string index, out UserIndexSubscription subscription) {
		if (!_userIndexes.TryGetValue(index, out var data))
			throw new Exception($"Failed to acquire write lock for index: {index}");

		if (!data.RWLock.TryEnterWriteLock(TimeSpan.FromMinutes(1)))
			throw new Exception($"Timed out when acquiring write lock for index: {index}");

		subscription = data.Subscription;
		return new WriteLock(data.RWLock);
	}

	private readonly record struct ReadLock(ReaderWriterLockSlim Lock) : IDisposable {
		public void Dispose() => Lock.ExitReadLock();
	}

	private readonly record struct WriteLock(ReaderWriterLockSlim Lock) : IDisposable {
		public void Dispose() {
			Lock.ExitWriteLock();
			Lock.Dispose(); // the write lock is used only once: when the user index is deleted
		}
	}
}
