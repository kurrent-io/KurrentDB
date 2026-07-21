// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using DotNext;
using DotNext.Runtime.InteropServices;
using DotNext.Threading;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Quack.Threading;
using Kurrent.Surge.Schema.Serializers.Json;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Scripting;
using KurrentDB.SecondaryIndexing.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal sealed class UserIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private static readonly IReadOnlyList<KeyValuePair<string, string>> NoFieldValues = [];

	private readonly Engine _engine = JintEngineFactory.CreateEngine(executionTimeout: TimeSpan.FromSeconds(30));
	private readonly JsRecordEvaluator _evaluator;
	private readonly Function? _filter;
	private readonly IReadOnlyList<IField> _fields;
	private readonly Function?[] _fieldSelectors;
	private readonly string?[] _fieldTexts;
	private readonly JsValue?[] _fieldValues;
	private readonly string _queryStreamName;
	private readonly IPublisher _publisher;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly UserIndexSql _sql;
	private readonly JsValue _skip;
	private readonly ILogger<UserIndexProcessor> _log;

	private ulong _sequenceId;
	private Atomic<TFPos> _lastPosition;
	private readonly BufferedView _appender;
	private bool _committing;

	public string IndexName { get; }

	public TFPos GetLastPosition() => _lastPosition.Value;
	public SecondaryIndexProgressTracker Tracker { get; }
	public UserIndexSql Sql => _sql;

	public UserIndexProcessor(
		string indexName,
		string jsEventFilter,
		IReadOnlyList<IField> fields,
		DuckDBConnectionPool db,
		UserIndexSql sql,
		IPublisher publisher,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)]
		Meter meter,
		Func<(long, DateTime)> getLastAppendedRecord,
		ILoggerFactory loggerFactory,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null) {
		IndexName = indexName;
		_sql = sql;
		_fields = fields;
		_log = loggerFactory.CreateLogger<UserIndexProcessor>();

		_queryStreamName = UserIndexHelpers.GetQueryStreamName(IndexName);

		_publisher = publisher;

		_engine.SetValue("skip", new object());
		_skip = _engine.Evaluate("skip");

		var serializerOptions = SystemJsonSchemaSerializerOptions.Default;

		_evaluator = new JsRecordEvaluator(_engine, serializerOptions);

		_filter = JsRecordEvaluator.Compile(_engine, jsEventFilter);
		_fieldSelectors = new Function?[fields.Count];
		for (var i = 0; i < fields.Count; i++)
			_fieldSelectors[i] = JsRecordEvaluator.Compile(_engine, fields[i].Selector);
		_fieldTexts = new string?[fields.Count];
		_fieldValues = new JsValue?[fields.Count];

		_connection = db.Open();
		_sql.CreateUserIndex(_connection);
		_appender = new(_connection, _sql.TableName, "log_position", _sql.ViewName);

		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";
		var tracker = new SecondaryIndexProgressTracker(indexName, serviceName, meter, clock ?? TimeProvider.System,
			loggerFactory.CreateLogger<SecondaryIndexProgressTracker>(), getLastAppendedRecord);

		var (lastPosition, lastTimestamp) = GetLastKnownRecord();
		_log.LogUserIndexLoadedLastKnownLogPosition(IndexName, lastPosition, lastTimestamp);
		tracker.InitLastIndexed(lastPosition.CommitPosition, lastTimestamp);

		_lastPosition.Write(in lastPosition);
		Tracker = tracker;
	}

	public BufferedView.Snapshot CaptureSnapshot(DuckDBAdvancedConnection connection)
		=> _appender.TakeSnapshot(connection, ExpandRecordFunction.UnnestExpression);

	public bool TryIndex(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return false;

		var eventPosition = resolvedEvent.OriginalPosition!.Value;

		if (!CanHandleEvent(resolvedEvent)) {
			_lastPosition.Write(in eventPosition);
			Tracker.RecordIndexed(resolvedEvent);
			return false;
		}

		var preparePosition = resolvedEvent.Event.LogPosition;
		var commitPosition = resolvedEvent.EventPosition?.CommitPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		var streamId = resolvedEvent.Event.EventStreamId;
		var created = new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds();
		var recordId = MemoryMarshal.AsReadOnlyBytes(in resolvedEvent.Event.EventId);

		IReadOnlyList<KeyValuePair<string, string>> fieldValues;

		var row = _appender.CreateRow();
		try {
			row.Add(preparePosition);

			if (commitPosition.HasValue && preparePosition != commitPosition)
				row.Add(commitPosition.Value);
			else
				row.Add(DBNull.Value);

			row.Add(eventNumber);
			row.Add(created);
			fieldValues = AppendFields(ref row);
			row.Add(recordId);
		} finally {
			row.Dispose();
		}

		_log.LogUserIndexIsAppendingEvent(IndexName, eventNumber, streamId, resolvedEvent.OriginalPosition, fieldValues.Count);

		_lastPosition.Write(in eventPosition);

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(_queryStreamName, fieldValues, resolvedEvent));

		Tracker.RecordIndexed(resolvedEvent);

		return true;
	}

	private bool CanHandleEvent(ResolvedEvent resolvedEvent) {
		try {
			_evaluator.MapRecord(resolvedEvent, ++_sequenceId);
			return _evaluator.Match(_filter) && TryExtractFieldValues();
		} catch (Exception ex) {
			_log.LogUserIndexFailedToProcessEvent(ex, IndexName, resolvedEvent.OriginalEventNumber, resolvedEvent.OriginalStreamId,
				resolvedEvent.OriginalPosition);
			return false;
		}
	}

	private IReadOnlyList<KeyValuePair<string, string>> AppendFields(ref BufferedAppender.Row row) {
		if (_fields.Count == 0)
			return NoFieldValues;

		var fieldValues = new List<KeyValuePair<string, string>>(_fields.Count);
		for (var i = 0; i < _fields.Count; i++) {
			var field = _fields[i];
			var text = _fieldTexts[i];
			if (text is null) {
				field.AppendNull(ref row);
			} else {
				field.AppendValue(_fieldValues[i]!, ref row);
				fieldValues.Add(new(field.Name, text));
			}
		}

		return fieldValues;
	}

	private bool TryExtractFieldValues() {
		var anyPresent = false;
		for (var i = 0; i < _fields.Count; i++) {
			var value = _evaluator.Select(_fieldSelectors[i]);
			if (_skip.Equals(value))
				return false;

			if (IsAbsent(value)) {
				_fieldTexts[i] = null;
				_fieldValues[i] = null;
			} else {
				// FormatValue also validates the type here (guarded phase): a bad value throws and drops the event
				_fieldTexts[i] = _fields[i].FormatValue(value!);
				_fieldValues[i] = value;
				anyPresent = true;
			}
		}

		return _fields.Count == 0 || anyPresent;
	}

	private static bool IsAbsent(JsValue? value) => value is null || value.IsNull() || value.IsUndefined();

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

		UserIndexSql.SetCheckpoint(_connection, checkpointArgs);
	}

	/// <summary>
	/// Commits all in-flight records to the index.
	/// </summary>
	public void Commit() {
		if (IsDisposed || !Interlocked.FalseToTrue(ref _committing))
			return;

		try {
			using var duration = Tracker.StartCommitDuration();
			_appender.Flush();
			_log.LogUserIndexCommitted(IndexName);
		} catch (Exception ex) {
			_log.LogUserIndexFailedToCommit(ex, IndexName);
			throw;
		} finally {
			Volatile.Write(ref _committing, false);
		}
	}

	protected override void Dispose(bool disposing) {
		_log.LogStoppingUserIndexProcessor(IndexName);
		if (disposing) {
			Commit();
			_appender.Unregister(_connection);
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

	[LoggerMessage(LogLevel.Trace, "User index: {index} is appending event: {eventNumber}@{stream} ({position}) with {fieldCount} field value(s).")]
	internal static partial void LogUserIndexIsAppendingEvent(this ILogger logger, string index, long eventNumber, string stream, TFPos? position, int fieldCount);

	[LoggerMessage(LogLevel.Error, "User index: {index} failed to process event: {eventNumber}@{streamId} ({position})")]
	internal static partial void LogUserIndexFailedToProcessEvent(this ILogger logger, Exception exception, string index, long eventNumber, string streamId, TFPos? position);

	[LoggerMessage(LogLevel.Trace, "User index: {index} loaded its checkpoint: {position} ({timestamp})")]
	internal static partial void LogUserIndexLoadedCheckpoint(this ILogger logger, string index, TFPos position, DateTimeOffset timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} loaded its last indexed position: {position} ({timestamp})")]
	internal static partial void LogUserIndexLoadedLastIndexedPosition(this ILogger logger, string index, TFPos position, DateTimeOffset timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} is checkpointing at: {position} ({timestamp})")]
	internal static partial void LogUserIndexIsCheckpointing(this ILogger logger, string index, TFPos position, DateTime timestamp);

	[LoggerMessage(LogLevel.Trace, "User index: {index} committed")]
	internal static partial void LogUserIndexCommitted(this ILogger logger, string index);

	[LoggerMessage(LogLevel.Error, "User index: {index} failed to commit")]
	internal static partial void LogUserIndexFailedToCommit(this ILogger logger, Exception exception, string index);

	[LoggerMessage(LogLevel.Trace, "Stopping user index processor for: {index}")]
	internal static partial void LogStoppingUserIndexProcessor(this ILogger logger, string index);
}
