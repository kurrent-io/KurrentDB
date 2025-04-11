using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using DuckDB.NET.Data;
using EventStore.Core.Metrics;
using Eventuous.Subscriptions.Context;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace EventStore.Core.Duck.Default;

class StreamIndex {
	readonly DuckDb _db;
	private readonly DuckDBConnection _connection;
	long _seq;
	readonly MemoryCache _streamCache = new(new MemoryCacheOptions());
	readonly MemoryCache _streamIdCache = new(new MemoryCacheOptions());
	readonly MemoryCacheEntryOptions _options = new() { SlidingExpiration = TimeSpan.FromMinutes(10) };

	public StreamIndex(DuckDb db) {
		const string sql = "select max(id) from streams";

		_connection = db.OpenConnection();
		_seq = _connection.Query<long?>(sql).SingleOrDefault() ?? 0;
		_appender = _connection.CreateAppender("streams");
		_db = db;
	}

	DuckDBAppender _appender;
	readonly SemaphoreSlim _semaphore = new(1);

	List<ReferenceRecord> _temp = [];

	public long Handle(IMessageConsumeContext ctx) {
		var name = ctx.Stream.ToString();
		if (_streamIdCache.TryGetValue(name, out var existing)) return (long)existing!;
		var fromDb = GetStreamIdFromDb(name);
		if (fromDb.HasValue) {
			_streamCache.Set(fromDb, name, _options);
			_streamIdCache.Set(name, fromDb, _options);
			return fromDb.Value;
		}

		var id = ++_seq;
		_semaphore.Wait();
		_streamIdCache.Set(name, id, _options);
		_streamCache.Set(id, name, _options);
		_temp.Add(new() { id = id, name = name });
		var row = _appender.CreateRow();
		row.AppendValue(id);
		row.AppendValue(name);
		row.AppendValue((int?)null);
		row.AppendValue((int?)null);
		row.EndRow();
		_semaphore.Release();
		return id;
	}

	public void Commit() {
		_semaphore.Wait();
		_appender.Dispose();
		_appender = _connection.CreateAppender("streams");
		_semaphore.Release();
	}

	public Dictionary<long, string> GetStreams(IEnumerable<long> ids) {
		var result = new Dictionary<long, string>();
		var uncached = new List<long>();
		foreach (var id in ids) {
			if (_streamCache.TryGetValue(id, out var name)) {
				result.Add(id, (string)name);
				continue;
			}

			uncached.Add(id);
		}

		return uncached.Count == 0 ? result : QueryStreams();

		[MethodImpl(MethodImplOptions.Synchronized)]
		Dictionary<long, string> QueryStreams() {
			while (true) {
				var duration = TempIndexMetrics.MeasureIndex("duck_get_streams");
				try {
					const string sql = "select * from streams where id in $ids";

					var connection = _db.GetOrOpenConnection();
					try {
						var records = connection.Query<ReferenceRecord>(sql, new { ids = uncached });
						foreach (var record in records) {
							_streamCache.Set(record.id, record.name, _options);
							result.Add(record.id, record.name);
						}
					} finally {
						_db.ReturnConnection(connection);
					}

					return result;
				} catch (Exception e) {
					Log.Warning("Error while querying category events: {Message}", e.Message);
					duration.SetException(e);
				} finally {
					duration.Dispose();
				}
			}
		}
	}

	long GetStreamId(string streamName) {
		return _streamCache.GetOrCreate(streamName, GetFromDb);

		long GetFromDb(ICacheEntry arg) {
			arg.SlidingExpiration = TimeSpan.FromMinutes(10);
			var id = GetStreamIdFromDb((string)arg.Key);
			return id ?? throw new InvalidOperationException($"Stream {arg.Key} not found");
		}
	}

	long? GetStreamIdFromDb(string streamName) {
		const string sql = "select id from streams where name=$name";

		var connection = _db.GetOrOpenConnection();
		try {
			return connection.Query<long?>(sql, new { name = streamName }).SingleOrDefault();
		} finally {
			_db.ReturnConnection(connection);
		}
	}

	static readonly string StreamSql = Sql.AppendIndexSql.Replace("{table}", "streams");
}
