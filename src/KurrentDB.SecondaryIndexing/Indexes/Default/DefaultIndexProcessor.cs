// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Common.Log;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexProcessor<TStreamId> : Disposable, ISecondaryIndexProcessor {
	private readonly DefaultIndex<TStreamId> _defaultIndex;
	private long _lastLogPosition;
	private ulong _seq;
	private int _page;
	private readonly Appender _appender;
	readonly DuckDBAdvancedConnection _connection;

	private static readonly ILogger Logger = Log.Logger.ForContext(nameof(DefaultIndexProcessor<TStreamId>));

	public long LastCommittedPosition { get; private set; }
	public long LastCommittedSequence { get; private set; }

	public DefaultIndexProcessor(DuckDbDataSource db, DefaultIndex<TStreamId> defaultIndex) {
		_connection = db.OpenNewConnection();
		_appender = new(_connection, "idx_all"u8);
		_defaultIndex = defaultIndex;

		var lastPosition = defaultIndex.GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", lastPosition);
		_seq = lastPosition.HasValue ? lastPosition.Value + 1 : 0;
	}

	public SequenceRecord Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return new(-1, -1);

		var category = _defaultIndex.CategoryIndex.Processor.Index(resolvedEvent);
		var eventType = _defaultIndex.EventTypeIndex.Processor.Index(resolvedEvent);
		var streamId = _defaultIndex.StreamIndex.Processor.Index(resolvedEvent);
		if (streamId == -1) {
			// StreamIndex is disposed
			return new(-1, -1);
		}

		using (var row = _appender.CreateRow()) {
			row.Append(_seq++);
			row.Append((int)resolvedEvent.Event.EventNumber);
			row.Append(resolvedEvent.Event.LogPosition);
			row.Append(resolvedEvent.Event.TimeStamp);
			row.Append(streamId);
			row.Append((int)eventType.Id);
			row.Append(eventType.Sequence);
			row.Append((int)category.Id);
			row.Append(category.Sequence);
		}

		_lastLogPosition = resolvedEvent.Event.LogPosition;
		_page++;
		return new((long)(_seq - 1), _lastLogPosition);
	}

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		_defaultIndex.CategoryIndex.Processor.Commit();
		_defaultIndex.EventTypeIndex.Processor.Commit();
		_defaultIndex.StreamIndex.Processor.Commit();

		_appender.Flush();
		Logger.Debug("Committed {Count} records to index at sequence {Seq}", _page, _seq);
		_page = 0;
		LastCommittedPosition = _lastLogPosition;
		LastCommittedSequence = (long)_seq - 1;
	}

	protected override void Dispose(bool disposing) {
		if (!IsDisposingOrDisposed) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
