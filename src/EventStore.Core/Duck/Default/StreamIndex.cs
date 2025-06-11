// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using Dapper;
using DotNext;
using DuckDB.NET.Data;
using Eventuous.Subscriptions.Context;
using Kurrent.Quack;
using Microsoft.Extensions.Caching.Memory;

namespace EventStore.Core.Duck.Default;

class StreamIndex : Disposable {
	readonly DuckDBAdvancedConnection _connection;
	readonly MemoryCache _streamIdCache = new(new MemoryCacheOptions());
	readonly MemoryCacheEntryOptions _options = new() { SlidingExpiration = TimeSpan.FromMinutes(10) };
	long _seq;

	public StreamIndex(DuckDbDataSource db) {
		const string sql = "select max(id) from streams";

		_connection = db.OpenNewConnection();
		_seq = _connection.Query<long?>(sql).SingleOrDefault() ?? 0;
		_appender = new(_connection, "streams"u8);
	}

	Appender _appender;
	readonly object _lock = new();

	public long Handle(IMessageConsumeContext ctx) {
		var name = ctx.Stream.ToString();
		if (_streamIdCache.TryGetValue(name, out var existing)) {
			return (long)existing!;
		}

		// var fromDb = GetStreamIdFromDb(name);
		var fromDb = _connection.QueryFirstOrDefault<StreamSql.GetStreamIdByNameQueryArgs, long, StreamSql.GetStreamIdByNameQuery>(new(name));
		if (fromDb.HasValue) {
			_streamIdCache.Set(name, fromDb, _options);
			return fromDb.Value;
		}

		var id = ++_seq;
		lock (_lock) {
			_streamIdCache.Set(name, id, _options);
			using var row = _appender.CreateRow();
			row.Append(id);
			row.Append(name);
			row.AppendDefault();
			row.AppendDefault();
		}

		return id;
	}

	public void Commit() {
		lock (_lock) {
			_appender.Flush();
			// _appender = _connection.CreateAppender("streams");
		}
	}

	// long? GetStreamIdFromDb(string streamName) {
	// 	const string sql = "select id from streams where name=$name";
	//
	// 	using var connection = _db.Pool.Open();
	// 	return connection.Query<long?>(sql, new { name = streamName }).SingleOrDefault();
	// }

	protected override void Dispose(bool disposing) {
		if (disposing) {
			lock (_lock) {
				_appender.Dispose();
				_connection.Dispose();
			}
		}

		base.Dispose(disposing);
	}
}
