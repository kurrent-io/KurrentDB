// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics;
using DotNext;
using DuckDB.NET.Data;
using EventStore.Common.Log;
using Kurrent.Quack;
using Serilog;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.Duck.Default;

public sealed class DefaultIndexHandler : Disposable {
	readonly DefaultIndex _defaultIndex;
	readonly DuckDBConnection _connection;

	ulong _seq;
	int _page;
	long _lastLogPosition;
	Appender _appender;
	// DuckDBAppender _appender;

	static readonly ILogger Logger = Log.Logger.ForContext("DefaultIndexHandler");

	public DefaultIndexHandler(DuckDbDataSource db, DefaultIndex defaultIndex) {
		_defaultIndex = defaultIndex;

		// separate connection for this component
		_connection = db.OpenNewConnection();
		// _appender = _connection.CreateAppender("idx_all");
		_appender = new(_connection, "idx_all"u8);
		var last = defaultIndex.GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", last);
		_seq = last.HasValue ? last.Value + 1 : 0;
	}

	public void HandleEvent(ResolvedEvent resolved) {
		var evt = resolved.OriginalEvent;
		if (evt.EventStreamId.StartsWith('$'))
			return;

		if (IsDisposingOrDisposed) return;

		var streamId = _defaultIndex.StreamIndex.Handle(evt);
		var et = _defaultIndex.EventTypeIndex.Handle(evt);
		var cat = _defaultIndex.CategoryIndex.Handle(evt);

		using var row = _appender.CreateRow();
		row.Append(_seq++);
		row.Append((int)evt.EventNumber);
		row.Append(evt.LogPosition);
		row.Append(new DateTimeOffset(evt.TimeStamp).ToUnixTimeMilliseconds());
		row.Append(streamId);
		row.Append((int)et.Id);
		row.Append(et.Sequence);
		row.Append((int)cat.Id);
		row.Append(cat.Sequence);

		_lastLogPosition = evt.LogPosition;
		_page++;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
			_defaultIndex.Dispose();
		}
		base.Dispose(disposing);
	}

	readonly Stopwatch _stopwatch = new();

	public void Commit() {
		if (_page == 0) return;
		_defaultIndex.StreamIndex.Commit();
		_stopwatch.Start();
		_appender.Flush();
		_stopwatch.Stop();
		Logger.Debug("Committed {Count} records to index at {Seq} in {Time} ms", _page, _seq, _stopwatch.ElapsedMilliseconds);
		_stopwatch.Reset();
		_page = 0;
		LastPosition = _lastLogPosition;
	}

	public long LastPosition { get; private set; }
	public long LastSequence => (long)_seq;
}
