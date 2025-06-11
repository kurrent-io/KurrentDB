// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using Dapper;
using DotNext;
using DuckDB.NET.Data;
using EventStore.Common.Log;
using EventStore.Core.Data;
using Eventuous.Subscriptions.Context;
using Kurrent.Quack;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using static EventStore.Core.Duck.Default.StreamSql;

namespace EventStore.Core.Duck.Default;

class StreamIndex : Disposable {
	readonly DuckDBAdvancedConnection _connection;
	readonly MemoryCache _streamIdCache = new(new MemoryCacheOptions());
	readonly MemoryCacheEntryOptions _options = new() { SlidingExpiration = TimeSpan.FromMinutes(10) };
	long _seq;

	static readonly ILogger Logger = Log.Logger.ForContext("StreamIndex");

	public StreamIndex(DuckDbDataSource db) {
		const string sql = "select max(id) from streams";

		_connection = db.OpenNewConnection();
		_seq = _connection.Query<long?>(sql).SingleOrDefault() ?? 0;
		_appender = new(_connection, "streams"u8);
	}

	Appender _appender;
	int _page;

	public long Handle(EventRecord evt) {
		var name = evt.EventStreamId;
		if (_streamIdCache.TryGetValue(name, out var existing)) {
			return (long)existing!;
		}

		var fromDb = _connection.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, long, GetStreamIdByNameQuery>(new(name));
		if (fromDb.HasValue) {
			_streamIdCache.Set(name, fromDb, _options);
			return fromDb.Value;
		}

		var id = ++_seq;
		_page++;
		_streamIdCache.Set(name, id, _options);
		using var row = _appender.CreateRow();
		row.Append(id);
		row.Append(name);
		row.AppendDefault();
		row.AppendDefault();

		return id;
	}

	readonly Stopwatch _stopwatch = new();

	public void Commit() {
		if (_page == 0) return;
		_stopwatch.Start();
		_appender.Flush();
		_stopwatch.Stop();
		Logger.Debug("Committed {Count} records to streams at {Seq} in {Time} ms", _page, _seq, _stopwatch.ElapsedMilliseconds);
		_stopwatch.Reset();
		_page = 0;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
