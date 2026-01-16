// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
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
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public class UserIndexEngineSubscription(
	ISystemClient client,
	IPublisher publisher,
	ISchemaSerializer serializer,
	SecondaryIndexingPluginOptions options,
	DuckDBConnectionPool db,
	IReadIndex<string> readIndex,
	Meter meter,
	Func<(long, DateTime)> getLastAppendedRecord,
	ILoggerFactory logFactory,
	CancellationToken token)
	: ISecondaryIndexReader {

	private readonly ConcurrentDictionary<string, UserIndexData> _userIndexes = new();
	private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
	private readonly ILogger<UserIndexEngineSubscription> _log = logFactory.CreateLogger<UserIndexEngineSubscription>();

	record struct UserIndexData(
		ReaderWriterLockSlim RWLock,
		UserIndexSubscription Subscription,
		SecondaryIndexReaderBase Reader);

	public bool CaughtUp { get; private set; }
	public long Checkpoint { get; private set; }

	public async Task Start() {
		try {
			await StartInternal();
		} catch (OperationCanceledException) {
			_log.LogSubscriptionToStreamIsShuttingDown(UserIndexConstants.ManagementAllStream);
		} catch (Exception ex) {
			_log.LogSubscriptionToStreamHasEncounteredAFatalError(ex, UserIndexConstants.ManagementAllStream);
		}
	}

	public async Task Stop() {
		using (_cts)
			await _cts.CancelAsync();

		foreach (var (index, data) in _userIndexes) {
			try {
				_log.LogStoppingUserIndex(LogLevel.Trace, index);
				await data.Subscription.Stop();
			} catch (Exception ex) {
				_log.LogFailedToStopUserIndex(ex, index);
			}
		}
	}

	class UserIndexReadState {
		public IndexCreated Created { get; set; } = null!;
		public bool Started { get; set; }
	}

	private async Task StartInternal() {
		_cts.Token.ThrowIfCancellationRequested();
		var channel = Channel.CreateBounded<ReadResponse>(new BoundedChannelOptions(capacity: 20));

		// Subscribe to all user index events
		// later we could subscribe to an index if we create one with system events
		await client.Subscriptions.SubscribeToStream(
			revision: StreamRevision.Start,
			stream: UserIndexConstants.ManagementAllStream,
			channel: channel,
			resiliencePipeline: ResiliencePipelines.RetryForever,
			cancellationToken: _cts.Token);

		Dictionary<string, UserIndexReadState> userIndexes = [];
		HashSet<string> deletedIndexes = [];

		await foreach (var response in channel.Reader.ReadAllAsync(_cts.Token)) {
			switch (response) {
				case ReadResponse.SubscriptionCaughtUp:
					if (CaughtUp)
						continue;

					_log.LogSubscriptionToStreamCaughtUp(UserIndexConstants.ManagementAllStream);

					CaughtUp = true;

					using (db.Rent(out var connection)) {
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

					_log.LogSubscriptionToStreamReceivedEventType(UserIndexConstants.ManagementAllStream, evt.OriginalEvent.EventType);

					var deserializedEvent = await serializer.Deserialize(
						data: evt.OriginalEvent.Data,
						schemaInfo: new(evt.OriginalEvent.EventType, SchemaDataFormat.Json));

					switch (deserializedEvent) {
						case IndexCreated x: {
							deletedIndexes.Remove(x.Name);
							userIndexes[x.Name] = new() {
								Created = x,
								Started = false,
							};
							break;
						}
						case IndexStarted x: {
							if (!userIndexes.TryGetValue(x.Name, out var state))
								break;

							state.Started = true;

							if (CaughtUp)
								await StartUserIndex(x.Name, state.Created);

							break;
						}
						case IndexStopped x: {
							if (!userIndexes.TryGetValue(x.Name, out var state))
								break;

							state.Started = false;

							if (CaughtUp)
								await StopUserIndex(x.Name);

							break;
						}
						case IndexDeleted x: {
							deletedIndexes.Add(x.Name);
							userIndexes.Remove(x.Name);

							if (CaughtUp)
								DeleteUserIndex(x.Name);

							break;
						}
						default:
							_log.LogSubscriptionToStreamReceivedUnknownEventType(UserIndexConstants.ManagementAllStream, evt.OriginalEvent.EventType, evt.OriginalEventNumber);
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
		IndexCreated createdEvent) where TField : IField {
		_log.LogStartingUserIndex(indexName);

		var inFlightRecords = new UserIndexInFlightRecords<TField>(options);

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
			db: db,
			sql: sql,
			inFlightRecords: inFlightRecords,
			publisher: publisher,
			meter: meter,
			getLastAppendedRecord: getLastAppendedRecord,
			loggerFactory: logFactory
		);

		var reader = new UserIndexReader<TField>(sharedPool: db, sql, inFlightRecords, readIndex);

		UserIndexSubscription subscription = new UserIndexSubscription<TField>(
			publisher: publisher,
			indexProcessor: processor,
			options: options,
			log: logFactory.CreateLogger<UserIndexSubscription>(),
			token: _cts.Token);

		_userIndexes.TryAdd(indexName, new(new(), subscription, reader));
		await subscription.Start();
	}

	private async Task StopUserIndex(string indexName) {
		_log.LogStoppingUserIndex(LogLevel.Debug, indexName);

		var writeLock = AcquireWriteLockForIndex(indexName, out var index);
		using (writeLock) {
			// we have the write lock, there are no readers. after we release the lock the index is no longer
			// in the dictionary so there can be no new readers.
			_userIndexes.TryRemove(indexName, out _);
		}

		// guaranteed no readers, we can stop.
		await index.Stop();

		DropSubscriptions(indexName);
	}

	private void DeleteUserIndex(string indexName) {
		_log.LogDeletingUserIndex(indexName);

		using (db.Rent(out var connection)) {
			DeleteUserIndexTable(connection, indexName);
		}

		DropSubscriptions(indexName);
	}

	private void DropSubscriptions(string indexName) {
		_log.LogDroppingSubscriptionsToUserIndex(indexName);
		publisher.Publish(new StorageMessage.SecondaryIndexDeleted(UserIndexHelpers.GetStreamNameRegex(indexName)));
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

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg,
		CancellationToken token) {
		UserIndexHelpers.ParseQueryStreamName(msg.IndexName, out var indexName, out _);
		_log.LogUserIndexReceivedReadForwardsRequest(indexName);
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

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg,
		CancellationToken token) {
		UserIndexHelpers.ParseQueryStreamName(msg.IndexName, out var indexName, out _);
		_log.LogUserIndexReceivedReadBackwardsRequest(indexName);
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
		return new(data.RWLock);
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

static partial class UserIndexEngineSubscriptionLogMessages {
	[LoggerMessage(LogLevel.Trace, "Subscription to: {stream} is shutting down.")]
	internal static partial void LogSubscriptionToStreamIsShuttingDown(this ILogger logger, string stream);

	[LoggerMessage(LogLevel.Critical, "Subscription to: {stream} has encountered a fatal error.")]
	internal static partial void LogSubscriptionToStreamHasEncounteredAFatalError(this ILogger logger, Exception exception, string stream);

	[LoggerMessage("Stopping user index: {index}")]
	internal static partial void LogStoppingUserIndex(this ILogger logger, LogLevel logLevel, string index);

	[LoggerMessage(LogLevel.Error, "Failed to stop user index: {index}")]
	internal static partial void LogFailedToStopUserIndex(this ILogger logger, Exception exception, string index);

	[LoggerMessage(LogLevel.Trace, "Subscription to: {stream} caught up")]
	internal static partial void LogSubscriptionToStreamCaughtUp(this ILogger logger, string stream);

	[LoggerMessage(LogLevel.Trace, "Subscription to: {stream} received event type: {type}")]
	internal static partial void LogSubscriptionToStreamReceivedEventType(this ILogger logger, string stream, string type);

	[LoggerMessage(LogLevel.Warning, "Subscription to: {stream} received unknown event type: {type} at event number: {eventNumber}")]
	internal static partial void LogSubscriptionToStreamReceivedUnknownEventType(this ILogger logger, string stream, string type, long eventNumber);

	[LoggerMessage(LogLevel.Debug, "Starting user index: {index}")]
	internal static partial void LogStartingUserIndex(this ILogger logger, string index);

	[LoggerMessage(LogLevel.Debug, "Deleting user index: {index}")]
	internal static partial void LogDeletingUserIndex(this ILogger logger, string index);

	[LoggerMessage(LogLevel.Trace, "Dropping subscriptions to user index: {index}")]
	internal static partial void LogDroppingSubscriptionsToUserIndex(this ILogger logger, string index);

	[LoggerMessage(LogLevel.Trace, "User index: {index} received read forwards request")]
	internal static partial void LogUserIndexReceivedReadForwardsRequest(this ILogger logger, string index);

	[LoggerMessage(LogLevel.Trace, "User index: {index} received read backwards request")]
	internal static partial void LogUserIndexReceivedReadBackwardsRequest(this ILogger logger, string index);
}
