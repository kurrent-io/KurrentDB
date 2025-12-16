// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Text.Json;
using DotNext;
using DotNext.Threading;
using DuckDB.NET.Data;
using DuckDB.NET.Data.DataChunk.Writer;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge;
using Kurrent.Surge.Schema.Serializers.Json;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.Surge;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal abstract class UserIndexProcessor : Disposable, ISecondaryIndexProcessor {
	protected static readonly ILogger Log = Serilog.Log.ForContext<UserIndexProcessor>();
	public abstract void Commit();
	public abstract bool TryIndex(SurgeRecord record);
	public abstract TFPos GetLastPosition();
	public abstract SecondaryIndexProgressTracker Tracker { get; }
}

internal class UserIndexProcessor<TField> : UserIndexProcessor where TField : IField {
	private readonly Engine _engine = new();
	private readonly Function? _filter;
	private readonly Function? _fieldSelector;
	private readonly UserIndexInFlightRecords<TField> _inFlightRecords;
	private readonly string _inFlightTableName;
	private readonly string _queryStreamName;
	private readonly IPublisher _publisher;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly UserIndexSql<TField> _sql;
	private readonly object _skip;
	private readonly JsonSerializerOptions _serializerOptions = SystemJsonSchemaSerializerOptions.Default;

	private TFPos _lastPosition;
	private Appender _appender;
	private Atomic.Boolean _committing;

	public string IndexName { get; }

	public override TFPos GetLastPosition() => _lastPosition;
	public override SecondaryIndexProgressTracker Tracker { get; }

	public UserIndexProcessor(
		string indexName,
		string jsEventFilter,
		string jsFieldSelector,
		DuckDBConnectionPool db,
		UserIndexSql<TField> sql,
		UserIndexInFlightRecords<TField> inFlightRecords,
		IPublisher publisher,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)]
		Meter meter,
		Func<(long, DateTime)> getLastAppendedRecord,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null) {
		IndexName = indexName;
		_sql = sql;

		_inFlightRecords = inFlightRecords;
		_inFlightTableName = UserIndexSql.GenerateInFlightTableNameFor(IndexName);
		_queryStreamName = UserIndexHelpers.GetQueryStreamName(IndexName);

		_publisher = publisher;

		_engine.SetValue("skip", new object());
		_skip = _engine.Evaluate("skip");

		if (jsEventFilter is not "")
			_filter = _engine.Evaluate(jsEventFilter).AsFunctionInstance();

		if (jsFieldSelector is not "")
			_fieldSelector = _engine.Evaluate(jsFieldSelector).AsFunctionInstance();

		_connection = db.Open();
		_sql.CreateUserIndex(_connection);
		RegisterTableFunction();

		_appender = new(_connection, _sql.TableNameUtf8.Span);

		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";
		var tracker = new SecondaryIndexProgressTracker(indexName, serviceName, meter, clock ?? TimeProvider.System, getLastAppendedRecord);

		(_lastPosition, var lastTimestamp) = GetLastKnownRecord();
		Log.Information("User index: {index} loaded its last known log position: {position} ({timestamp})", IndexName, _lastPosition, lastTimestamp);
		tracker.InitLastIndexed(_lastPosition.CommitPosition, lastTimestamp);

		Tracker = tracker;
	}

	public override bool TryIndex(SurgeRecord record) {
		if (IsDisposingOrDisposed)
			return false;

		var canHandle = CanHandleRecord(record, out var field);
		var position = record.LogPosition.ToTFPos();
		_lastPosition = position;

		if (!canHandle) {
			Tracker.RecordIndexed(position.CommitPosition, record.Timestamp);
			return false;
		}

		var preparePosition = position.PreparePosition;
		var commitPosition = position.CommitPosition;
		var eventNumber = record.Position.StreamRevision.Value;
		var streamId = record.StreamId.Value;
		var created = new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds();
		var fieldStr = field?.ToString();

		Log.Verbose("User index: {index} is appending event: {eventNumber}@{stream} ({position}). Field = {field}.",
			IndexName, eventNumber, streamId, position, field);

		using (var row = _appender.CreateRow()) {
			row.Append(preparePosition);

			if (preparePosition != commitPosition)
				row.Append(commitPosition);
			else
				row.Append(DBNull.Value);

			row.Append(eventNumber);
			row.Append(created);
			field?.AppendTo(row);
		}

		_inFlightRecords.Append(preparePosition, commitPosition, eventNumber, field, created);

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(_queryStreamName, resolvedEvent));
		if (field is not null)
			_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(UserIndexHelpers.GetQueryStreamName(IndexName, fieldStr), resolvedEvent));

		Tracker.RecordIndexed(position.CommitPosition, record.Timestamp);

		return true;
	}

	private bool CanHandleRecord(SurgeRecord record, out TField? field) {
		field = default;

		try {
			var jsRecord = record.MapToJsRecord(_serializerOptions);
			var jsObject = JsValue.FromObject(_engine, jsRecord);

			if (_filter is not null) {
				var passesFilter = _filter.Call(jsObject).AsBoolean();
				if (!passesFilter)
					return false;
			}

			if (_fieldSelector is not null) {
				var fieldJsValue = _fieldSelector.Call(jsObject);
				if (_skip.Equals(fieldJsValue))
					return false;
				field = (TField) TField.ParseFrom(fieldJsValue);
			}

			return true;
		} catch(Exception ex) {
			Log.Error(ex, "User index: {index} failed to process event: {eventNumber}@{streamId} ({position})",
				IndexName, record.Position.StreamRevision.Value, record.StreamId, record.LogPosition);
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
			Log.Verbose("User index: {index} loaded its checkpoint: {position} ({timestamp})", IndexName, pos, timestamp);

			result = (pos, timestamp);
		}

		var lastIndexed = _sql.GetLastIndexedRecord(_connection);
		if (lastIndexed != null) {
			var pos = new TFPos(lastIndexed.Value.CommitPosition ?? lastIndexed.Value.PreparePosition, lastIndexed.Value.PreparePosition);
			var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(lastIndexed.Value.Timestamp);
			Log.Verbose("User index: {index} loaded its last indexed position: {position} ({timestamp})", IndexName, pos, timestamp);

			if (pos > result.pos)
				result = (pos, timestamp);
		}

		return result;
	}

	public void Checkpoint(TFPos position, DateTime timestamp) {
		Log.Verbose("User index: {index} is checkpointing at: {position} ({timestamp})", IndexName, position, timestamp);

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
			Log.Verbose("User index: {index} committed {count} records", IndexName, _inFlightRecords.Count);
		} catch (Exception ex) {
			Log.Error(ex, "User index: {index} failed to commit {count} records", IndexName, _inFlightRecords.Count);
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

			if (TField.Type is { } type)
				columnInfos.Add(new ColumnInfo(_sql.FieldColumnName, type));

			return new(columnInfos, records);
		}

		void MapperCallback(object? item, IDuckDBDataWriter[] writers, ulong rowIndex) {
			var record = (UserIndexInFlightRecord<TField>)item!;
			writers[0].WriteValue(record.LogPosition, rowIndex);
			writers[1].WriteValue(record.EventNumber, rowIndex);
			writers[2].WriteValue(record.Created, rowIndex);

			if (TField.Type is not null) {
				record.Field!.WriteTo(writers[3], rowIndex);
			}
		}
#pragma warning restore DuckDBNET001
	}

	public void GetUserIndexTableDetails(out string tableName, out string inFlightTableName, out bool hasFields) {
		tableName = _sql.TableName;
		inFlightTableName = _inFlightTableName;
		hasFields = TField.Type is not null;
	}

	protected override void Dispose(bool disposing) {
		Log.Verbose("Stopping user index processor for: {index}", IndexName);
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
			_engine.Dispose();
		}

		base.Dispose(disposing);
	}
}
