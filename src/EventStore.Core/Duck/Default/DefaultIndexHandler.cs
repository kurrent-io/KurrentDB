// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DuckDB.NET.Data;
using EventStore.Common.Log;
using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Context;
using Kurrent.Quack;
using Serilog;

namespace EventStore.Core.Duck.Default;

public sealed class DefaultIndexHandler<TStreamId> : IEventHandler, IDisposable {
	readonly DefaultIndex<TStreamId> _defaultIndex;
	readonly DuckDBConnection _connection;
	readonly object _lock = new();

	ulong _seq;
	int _page;
	long _lastLogPosition;
	Appender _appender;
	// DuckDBAppender _appender;

	static readonly ILogger Logger = Log.Logger.ForContext("DefaultIndexHandler");

	public DefaultIndexHandler(DuckDbDataSource db, DefaultIndex<TStreamId> defaultIndex) {
		_defaultIndex = defaultIndex;

		// separate connection for this component
		_connection = db.OpenNewConnection();
		// _appender = _connection.CreateAppender("idx_all");
		_appender = new(_connection, "idx_all"u8);
		var last = defaultIndex.GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", last);
		_seq = last.HasValue ? last.Value + 1 : 0;
	}

	public ValueTask<EventHandlingStatus> HandleEvent(IMessageConsumeContext context) {
		if (context.Stream.ToString().StartsWith('$'))
			return ValueTask.FromResult(EventHandlingStatus.Ignored);

		if (_appenderDisposed || _disposing) return new(EventHandlingStatus.Ignored);

		var streamId = _defaultIndex.StreamIndex.Handle(context);
		var et = _defaultIndex.EventTypeIndex.Handle(context);
		var cat = _defaultIndex.CategoryIndex.Handle(context);

		lock (_lock) {
			using var row = _appender.CreateRow();
			row.Append(_seq++);
			row.Append((int)context.EventNumber);
			row.Append(context.GlobalPosition);
			row.Append(new DateTimeOffset(context.Created).ToUnixTimeMilliseconds());
			row.Append(streamId);
			row.Append((int)et.Id);
			row.Append(et.Sequence);
			row.Append((int)cat.Id);
			row.Append(cat.Sequence);
		}

		_lastLogPosition = (long)context.GlobalPosition;
		_page++;

		return ValueTask.FromResult(EventHandlingStatus.Success);
	}

	public string DiagnosticName => "DefaultIndexHandler";

	bool _disposed;
	bool _disposing;
	bool _appenderDisposed;

	public void Dispose() {
		if (_disposing || _disposed) return;
		_disposing = true;
		Commit(false);
		_disposed = true;
		_appender.Dispose();
		_connection.Dispose();
		_defaultIndex.Dispose();
	}

	public bool NeedsCommitting => _page > 0;

	readonly Stopwatch _stopwatch = new();

	public void Commit(bool reopen = true) {
		if (_appenderDisposed || _page == 0) return;
		lock (_lock) {
			_stopwatch.Start();
			_appender.Flush();
			_stopwatch.Stop();
			Logger.Debug("Committed {Count} records to index at {Seq} in {Time} ms", _page, _seq, _stopwatch.ElapsedMilliseconds);
			_stopwatch.Reset();
			_page = 0;
			if (!reopen) return;
			// _appender = _connection.CreateAppender("idx_all");
			LastPosition = _lastLogPosition;
		}

		_appenderDisposed = false;
	}

	public long LastPosition { get; private set; }
	public long LastSequence => (long)_seq;
}
