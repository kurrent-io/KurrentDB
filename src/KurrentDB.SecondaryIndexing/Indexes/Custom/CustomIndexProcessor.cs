// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Text;
using DotNext;
using DotNext.Threading;
using DuckDB.NET.Data;
using DuckDB.NET.Data.DataChunk.Writer;
using Jint;
using Jint.Native.Function;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal abstract class CustomIndexProcessor: Disposable, ISecondaryIndexProcessor {
	protected static readonly ILogger Log = Serilog.Log.ForContext<CustomIndexProcessor>();
	public abstract void Commit();
	public abstract bool TryIndex(ResolvedEvent evt);
	public abstract TFPos GetLastPosition();
	public abstract SecondaryIndexProgressTracker Tracker { get; }
}

internal class CustomIndexProcessor<TPartitionKey> : CustomIndexProcessor where TPartitionKey : ITPartitionKey {
	private readonly DuckDBConnectionPool _db;
	private readonly Function _eventFilter;
	private readonly Function? _partitionKeySelector;
	private readonly ResolvedEventJsObject _resolvedEventJsObject;
	private readonly IndexInFlightRecords _inFlightRecords;
	private readonly string _inFlightTableName;
	private readonly IPublisher _publisher;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly CustomIndexSql<TPartitionKey> _sql;

	private TFPos _lastPosition;
	private Appender _appender;
	private Atomic.Boolean _committing;

	public string IndexName { get; }

	public override TFPos GetLastPosition() => _lastPosition;
	public override SecondaryIndexProgressTracker Tracker { get; }

	public CustomIndexProcessor(
		string indexName,
		string jsEventFilter,
		string jsPartitionKeySelector,
		DuckDBConnectionPool db,
		CustomIndexSql<TPartitionKey> sql,
		IndexInFlightRecords inFlightRecords,
		IPublisher publisher,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)]
		Meter meter,
		Func<(long, DateTime)> getLastAppendedRecord,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null) {
		IndexName = indexName;
		_db = db;
		_sql = sql;

		_inFlightRecords = inFlightRecords;
		_inFlightTableName = CustomIndexSql.GenerateInFlightTableNameFor(IndexName);

		_publisher = publisher;

		var engine = new Engine();
		_eventFilter = engine.Evaluate(jsEventFilter).AsFunctionInstance();
		if (jsPartitionKeySelector != string.Empty)
			_partitionKeySelector = engine.Evaluate(jsPartitionKeySelector).AsFunctionInstance();

		_resolvedEventJsObject = new(engine);

		_connection = db.Open();
		_sql.CreateCustomIndex(_connection);
		RegisterTableFunction();

		_appender = new(_connection, _sql.TableName.Span);

		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";
		var tracker = new SecondaryIndexProgressTracker(indexName, serviceName, meter, clock ?? TimeProvider.System, getLastAppendedRecord);

		(_lastPosition, var lastTimestamp) = GetLastKnownRecord();
		Log.Information("Custom index: {index} loaded its last known log position: {position} ({timestamp})", IndexName, _lastPosition, lastTimestamp);
		tracker.InitLastIndexed(_lastPosition.CommitPosition, lastTimestamp);

		Tracker = tracker;
	}

	public override bool TryIndex(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return false;

		var canHandle = CanHandleEvent(resolvedEvent, out var partitionKey);
		_lastPosition = resolvedEvent.OriginalPosition!.Value;

		if (!canHandle) {
			Tracker.RecordIndexed(resolvedEvent);
			return false;
		}

		var preparePosition = resolvedEvent.Event.LogPosition;
		var commitPosition = resolvedEvent.EventPosition?.CommitPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		var streamId = resolvedEvent.Event.EventStreamId;
		var created = new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds();
		var partitionKeyStr = partitionKey?.ToString();

		Log.Verbose("Custom index: {index} is appending event: {eventNumber}@{stream} ({position}). Partition key = {partitionKey}.",
			IndexName, eventNumber, streamId, resolvedEvent.OriginalPosition, partitionKeyStr);

		using (var row = _appender.CreateRow()) {
			row.Append(preparePosition);

			if (commitPosition.HasValue && preparePosition != commitPosition)
				row.Append(commitPosition.Value);
			else
				row.Append(DBNull.Value);

			row.Append(eventNumber);
			row.Append(created);
			partitionKey?.AppendTo(row);
		}

		_inFlightRecords.Append(preparePosition, commitPosition ?? preparePosition, eventNumber, partitionKeyStr, created);

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(CustomIndex.GetStreamName(IndexName), resolvedEvent));
		if (partitionKey is not null)
			_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(CustomIndex.GetStreamName(IndexName, partitionKeyStr), resolvedEvent));

		Tracker.RecordIndexed(resolvedEvent);

		return true;
	}

	private bool CanHandleEvent(ResolvedEvent resolvedEvent, out ITPartitionKey? partitionKey) {
		partitionKey = null;

		try {
			_resolvedEventJsObject.StreamId = resolvedEvent.OriginalEvent.EventStreamId;
			_resolvedEventJsObject.EventId = $"{resolvedEvent.OriginalEvent.EventId}";
			_resolvedEventJsObject.EventNumber = resolvedEvent.OriginalEvent.EventNumber;
			_resolvedEventJsObject.EventType = resolvedEvent.OriginalEvent.EventType;
			_resolvedEventJsObject.IsJson = resolvedEvent.OriginalEvent.IsJson;
			_resolvedEventJsObject.Data = resolvedEvent.OriginalEvent.Data;
			_resolvedEventJsObject.Metadata = resolvedEvent.OriginalEvent.Metadata;

			var passesFilter = _eventFilter.Call(_resolvedEventJsObject).AsBoolean();
			if (!passesFilter)
				return false;

			if (_partitionKeySelector is not null) {
				var partitionKeyJsValue = _partitionKeySelector.Call(_resolvedEventJsObject);
				partitionKey = TPartitionKey.ParseFrom(partitionKeyJsValue);
			}

			return true;
		} catch(Exception ex) {
			Log.Error(ex, "Custom index: {index} failed to process event: {eventNumber}@{streamId} ({position})",
				IndexName, resolvedEvent.OriginalEventNumber, resolvedEvent.OriginalStreamId, resolvedEvent.OriginalPosition);
			return false;
		}
	}

	private (TFPos, DateTimeOffset) GetLastKnownRecord() {
		(TFPos pos, DateTimeOffset timestamp) result = (TFPos.Invalid, DateTimeOffset.MinValue);

		var checkpointArgs = new GetCheckpointQueryArgs(IndexName);
		var checkpoint = _sql.GetCheckpoint(_connection, checkpointArgs);
		if (checkpoint != null) {
			var pos = new TFPos(checkpoint.Value.CommitPosition ?? checkpoint.Value.PreparePosition, checkpoint.Value.PreparePosition);
			var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(checkpoint.Value.Timestamp);
			Log.Verbose("Custom index: {index} loaded its checkpoint: {position} ({timestamp})", IndexName, pos, timestamp);

			result = (pos, timestamp);
		}

		var lastIndexed = _sql.GetLastIndexedRecord(_connection);
		if (lastIndexed != null) {
			var pos = new TFPos(lastIndexed.Value.CommitPosition ?? lastIndexed.Value.PreparePosition, lastIndexed.Value.PreparePosition);
			var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(lastIndexed.Value.Timestamp);
			Log.Verbose("Custom index: {index} loaded its last indexed position: {position} ({timestamp})", IndexName, pos, timestamp);

			if (pos > result.pos)
				result = (pos, timestamp);
		}

		return result;
	}

	public void Checkpoint(TFPos position, DateTime timestamp) {
		Log.Verbose("Custom index: {index} is checkpointing at: {position} ({timestamp})", IndexName, position, timestamp);

		var checkpointArgs = new SetCheckpointQueryArgs {
			IndexName = IndexName,
			PreparePosition = position.PreparePosition,
			CommitPosition = position.PreparePosition == position.CommitPosition ? null : position.CommitPosition,
			Created = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds()
		};

		_sql.SetCheckpoint(_connection, checkpointArgs);
	}

	public override void Commit() => Commit(true);

	/// <summary>
	/// Commits all in-flight records to the index.
	/// </summary>
	/// <param name="clearInflight">Tells you whether to clear the in-flight records after committing. It must be true and only set to false in tests.</param>
	private void Commit(bool clearInflight) {
		if (IsDisposed || !_committing.FalseToTrue())
			return;

		try {
			using var duration = Tracker.StartCommitDuration();
			_appender.Flush();
			Log.Verbose("Custom index: {index} committed {count} records", IndexName, _inFlightRecords.Count);
		} catch (Exception ex) {
			Log.Error(ex, "Custom index: {index} failed to commit {count} records", IndexName, _inFlightRecords.Count);
			throw;
		} finally {
			_committing.TrueToFalse();
		}

		if (clearInflight) {
			_inFlightRecords.Clear();
		}
	}

	private void RegisterTableFunction() {
#pragma warning disable DuckDBNET001
		_connection.RegisterTableFunction(_inFlightTableName, ResultCallback, MapperCallback);

		TableFunction ResultCallback() {
			var records = _inFlightRecords.GetInFlightRecords();
			List<ColumnInfo> columnInfos = [
				new("log_position", typeof(long)),
				new("event_number", typeof(long)),
				new("created", typeof(long)),
			];

			if (TPartitionKey.Type is { } type)
				columnInfos.Add(new ColumnInfo("partition_key", type));

			return new(columnInfos, records);
		}

		void MapperCallback(object? item, IDuckDBDataWriter[] writers, ulong rowIndex) {
			var record = (InFlightRecord)item!;
			writers[0].WriteValue(record.LogPosition, rowIndex);
			writers[1].WriteValue(record.EventNumber, rowIndex);
			writers[2].WriteValue(record.Created, rowIndex);

			if (TPartitionKey.Type is { } type) {
				var value = Convert.ChangeType(record.PartitionKey, type);
				writers[3].WriteValue(value, rowIndex);
			}
		}
#pragma warning restore DuckDBNET001
	}

	public void GetCustomIndexTableNames(out string tableName, out string inFlightTableName, out bool hasPartitionKey) {
		tableName = Encoding.UTF8.GetString(_sql.TableName.Span);
		inFlightTableName = _inFlightTableName;
		hasPartitionKey = TPartitionKey.Type is not null;
	}

	protected override void Dispose(bool disposing) {
		Log.Verbose("Stopping custom index processor for: {index}", IndexName);
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
