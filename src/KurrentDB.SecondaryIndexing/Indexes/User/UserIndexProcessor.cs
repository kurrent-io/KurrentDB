// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using DotNext;
using DotNext.Threading;
using DuckDB.NET.Data;
using DuckDB.NET.Data.DataChunk.Writer;
using Jint;
using Jint.Native;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge.Schema.Serializers.Json;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Surge;
using KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal abstract class UserIndexProcessor : Disposable, ISecondaryIndexProcessor {
	public abstract void Commit();
	public abstract bool TryIndex(ResolvedEvent evt);
	public abstract TFPos GetLastPosition();
	public abstract SecondaryIndexProgressTracker Tracker { get; }
}

internal class UserIndexProcessor<TField> : UserIndexProcessor where TField : IField {
	private readonly Engine _engine = JintEngineFactory.CreateEngine(executionTimeout: TimeSpan.FromSeconds(30));
	private readonly JsRecordEvaluator _evaluator;
	private readonly string? _filterExpression;
	private readonly string? _fieldSelectorExpression;
	private readonly JsRecord _jsRecord;
	private readonly UserIndexInFlightRecords<TField> _inFlightRecords;
	private readonly string _inFlightTableName;
	private readonly string _queryStreamName;
	private readonly IPublisher _publisher;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly UserIndexSql<TField> _sql;
	private readonly object _skip;
	private ulong _sequenceId;
	private readonly ILogger<UserIndexProcessor> _log;

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
		ILoggerFactory loggerFactory,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null) {
		IndexName = indexName;
		_sql = sql;
		_log = loggerFactory.CreateLogger<UserIndexProcessor>();

		_inFlightRecords = inFlightRecords;
		_inFlightTableName = UserIndexSql.GenerateInFlightTableNameFor(IndexName);
		_queryStreamName = UserIndexHelpers.GetQueryStreamName(IndexName);

		_publisher = publisher;

		_engine.SetValue("skip", new object());
		_skip = _engine.Evaluate("skip");

		var serializerOptions = SystemJsonSchemaSerializerOptions.Default;

		_evaluator = new JsRecordEvaluator(_engine, serializerOptions);

		_filterExpression = jsEventFilter is not "" ? jsEventFilter : null;
		_fieldSelectorExpression = jsFieldSelector is not "" ? jsFieldSelector : null;

		_jsRecord = new JsRecord();

		_connection = db.Open();
		_sql.CreateUserIndex(_connection);
		RegisterTableFunction();

		_appender = new(_connection, _sql.TableNameUtf8.Span);

		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";
		var tracker = new SecondaryIndexProgressTracker(indexName, serviceName, meter, clock ?? TimeProvider.System,
			loggerFactory.CreateLogger<SecondaryIndexProgressTracker>(), getLastAppendedRecord);

		(_lastPosition, var lastTimestamp) = GetLastKnownRecord();
		_log.LogUserIndexLoadedLastKnownLogPosition(IndexName, _lastPosition, lastTimestamp);
		tracker.InitLastIndexed(_lastPosition.CommitPosition, lastTimestamp);

		Tracker = tracker;
	}

	public override bool TryIndex(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return false;

		var canHandle = CanHandleEvent(resolvedEvent, out var field);
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
		var fieldStr = field?.ToString();

		_log.LogUserIndexIsAppendingEvent(IndexName, eventNumber, streamId, resolvedEvent.OriginalPosition, fieldStr);

		using (var row = _appender.CreateRow()) {
			row.Append(preparePosition);

			if (commitPosition.HasValue && preparePosition != commitPosition)
				row.Append(commitPosition.Value);
			else
				row.Append(DBNull.Value);

			row.Append(eventNumber);
			row.Append(created);
			field?.AppendTo(row);
		}

		_inFlightRecords.Append(preparePosition, commitPosition ?? preparePosition, eventNumber, field, created);

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(_queryStreamName, resolvedEvent));
		if (field is not null)
			_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(UserIndexHelpers.GetQueryStreamName(IndexName, fieldStr),
				resolvedEvent));

		Tracker.RecordIndexed(resolvedEvent);

		return true;
	}

	bool CanHandleEvent(ResolvedEvent resolvedEvent, out TField? field) {
		field = default;

		try {
			_evaluator.MapRecord(resolvedEvent, ++_sequenceId);

			if (!_evaluator.Match(_filterExpression))
				return false;

			var fieldValue = _evaluator.Select(_fieldSelectorExpression);
			if (fieldValue == JsValue.Null)
				return true;

			if (_skip.Equals(fieldValue))
				return false;

			field = (TField)TField.ParseFrom(fieldValue);

			return true;
		} catch (Exception ex) {
			_log.LogUserIndexFailedToProcessEvent(ex, IndexName, resolvedEvent.OriginalEventNumber, resolvedEvent.OriginalStreamId,
				resolvedEvent.OriginalPosition);
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
			_log.LogUserIndexLoadedCheckpoint(IndexName, pos, timestamp);

			result = (pos, timestamp);
		}

		var lastIndexed = _sql.GetLastIndexedRecord(_connection);
		if (lastIndexed != null) {
			var pos = new TFPos(lastIndexed.Value.CommitPosition ?? lastIndexed.Value.PreparePosition, lastIndexed.Value.PreparePosition);
			var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(lastIndexed.Value.Timestamp);
			_log.LogUserIndexLoadedLastIndexedPosition(IndexName, pos, timestamp);

			if (pos > result.pos)
				result = (pos, timestamp);
		}

		return result;
	}

	public void Checkpoint(TFPos position, DateTime timestamp) {
		_log.LogUserIndexIsCheckpointing(IndexName, position, timestamp);

		var checkpointArgs = new SetCheckpointQueryArgs {
			IndexName = IndexName,
			PreparePosition = position.PreparePosition,
			CommitPosition = position.PreparePosition == position.CommitPosition ? null : position.CommitPosition,
			Created = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds()
		};

		UserIndexSql<TField>.SetCheckpoint(_connection, checkpointArgs);
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
			_log.LogUserIndexCommitted(IndexName, _inFlightRecords.Count);
		} catch (Exception ex) {
			_log.LogUserIndexFailedToCommit(ex, IndexName, _inFlightRecords.Count);
			throw;
		} finally {
			_committing.TrueToFalse();
		}

		if (clearInflight) {
			_inFlightRecords.Clear();
		}
	}

	private void RegisterTableFunction() {
		_connection.RegisterTableFunction(_inFlightTableName, ResultCallback, MapperCallback);

		TableFunction ResultCallback() {
			var records = _inFlightRecords.GetInFlightRecords();
			List<ColumnInfo> columnInfos = [
				new("log_position", typeof(long)),
				new("event_number", typeof(long)),
				new("created", typeof(long)),
			];

			if (TField.Type is { } type)
				columnInfos.Add(new(_sql.FieldColumnName, type));

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
	}

	public void GetUserIndexTableDetails(out string tableName, out string inFlightTableName, out string? fieldName) {
		tableName = _sql.TableName;
		inFlightTableName = _inFlightTableName;
		fieldName = _sql.FieldColumnName;
	}

	protected override void Dispose(bool disposing) {
		_log.LogStoppingUserIndexProcessor(IndexName);
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
			_engine.Dispose();
		}

		base.Dispose(disposing);
	}
}

static partial class UserIndexProcessorLogMessages {
	[LoggerMessage(LogLevel.Information, "User index: {index} loaded its last known log position: {position} ({timestamp})")]
	internal static partial void LogUserIndexLoadedLastKnownLogPosition(this ILogger logger, string index, TFPos position, DateTimeOffset timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} is appending event: {eventNumber}@{stream} ({position}). Field = {field}.")]
	internal static partial void LogUserIndexIsAppendingEvent(this ILogger logger, string index, long eventNumber, string stream, TFPos? position, string? field);

	[LoggerMessage(LogLevel.Error, "User index: {index} failed to process event: {eventNumber}@{streamId} ({position})")]
	internal static partial void LogUserIndexFailedToProcessEvent(this ILogger logger, Exception exception, string index, long eventNumber, string streamId, TFPos? position);

	[LoggerMessage(LogLevel.Trace, "User index: {index} loaded its checkpoint: {position} ({timestamp})")]
	internal static partial void LogUserIndexLoadedCheckpoint(this ILogger logger, string index, TFPos position, DateTimeOffset timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} loaded its last indexed position: {position} ({timestamp})")]
	internal static partial void LogUserIndexLoadedLastIndexedPosition(this ILogger logger, string index, TFPos position, DateTimeOffset timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} is checkpointing at: {position} ({timestamp})")]
	internal static partial void LogUserIndexIsCheckpointing(this ILogger logger, string index, TFPos position, DateTime timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} committed {count} records")]
	internal static partial void LogUserIndexCommitted(this ILogger logger, string index, int count);

	[LoggerMessage(LogLevel.Error, "User index: {index} failed to commit {count} records")]
	internal static partial void LogUserIndexFailedToCommit(this ILogger logger, Exception exception, string index, int count);

	[LoggerMessage(LogLevel.Trace, "Stopping user index processor for: {index}")]
	internal static partial void LogStoppingUserIndexProcessor(this ILogger logger, string index);
}
