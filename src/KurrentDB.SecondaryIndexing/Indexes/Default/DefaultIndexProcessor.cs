// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly DefaultIndex _defaultIndex;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly InFlightRecord[] _inFlightRecords;

	private int _inFlightRecordsCount;
	private Appender _appender;

	private static readonly ILogger Logger = Log.Logger.ForContext<DefaultIndexProcessor>();

	public long LastIndexedPosition { get; private set; }
	public long LastSequence;

	public DefaultIndexProcessor(DuckDbDataSource db, DefaultIndex defaultIndex, int commitBatchSize) {
		_connection = db.OpenNewConnection();
		_appender = new Appender(_connection, "idx_all"u8);
		_defaultIndex = defaultIndex;
		_inFlightRecords = new InFlightRecord[commitBatchSize];

		var lastSequence = defaultIndex.GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", lastSequence);
		LastSequence = lastSequence.HasValue ? lastSequence.Value + 1 : 0;
	}

	public void Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed) return;

		var category = _defaultIndex.CategoryIndex.Processor.Index(resolvedEvent);
		var eventType = _defaultIndex.EventTypeIndex.Processor.Index(resolvedEvent);
		var streamId = _defaultIndex.StreamIndex.Processor.Index(resolvedEvent);
		if (streamId == -1) {
			// StreamIndex is disposed
			return;
		}

		var sequence = LastSequence++;
		var logPosition = resolvedEvent.Event.LogPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		using (var row = _appender.CreateRow()) {
			row.Append(sequence);
			row.Append(eventNumber);
			row.Append(logPosition);
			row.Append(new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds());
			row.Append(streamId);
			row.Append(eventType.Id);
			row.Append(eventType.Sequence);
			row.Append(category.Id);
			row.Append(category.Sequence);
		}

		_inFlightRecords[_inFlightRecordsCount]
			= new(
				sequence,
				logPosition,
				eventNumber,
				category.Id,
				category.Sequence,
				eventType.Id,
				eventType.Sequence
			);
		LastIndexedPosition = resolvedEvent.Event.LogPosition;
		_inFlightRecordsCount++;
	}

	private readonly Stopwatch _sw = new();

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		_defaultIndex.StreamIndex.Processor.Commit();

		try {
			_sw.Restart();
			_appender.Flush();
			_sw.Stop();
			Logger.Debug("Committed {Count} records to index at seq {Seq} ({Took} ms)", _inFlightRecordsCount, LastSequence, _sw.ElapsedMilliseconds);
		} catch (Exception e) {
			Logger.Error(e, "Failed to commit {Count} records to index at sequence {Seq}", _inFlightRecordsCount, LastSequence);
			throw;
		}

		_inFlightRecordsCount = 0;
	}

	public IEnumerable<AllRecord> TryGetInFlightRecords(long fromSeq, long toSeq) {
		for (var i = 0; i < _inFlightRecordsCount; i++) {
			var current = _inFlightRecords[i];
			if (current.Seq > toSeq) yield break;
			if (current.Seq >= fromSeq && current.Seq <= toSeq) {
				yield return new(current.Seq, current.LogPosition);
			}
		}
	}

	public IEnumerable<T> QueryInFlightRecords<T>(Func<InFlightRecord, bool> query, Func<InFlightRecord, T> map) {
		for (var i = 0; i < _inFlightRecordsCount; i++) {
			var current = _inFlightRecords[i];
			if (query(current)) yield return map(current);
		}
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
