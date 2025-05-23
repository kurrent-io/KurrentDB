// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Caching.Memory;
using static KurrentDB.SecondaryIndexing.Indices.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Indices.Stream;

internal class StreamIndexProcessor(DuckDBAdvancedConnection connection) : Disposable, ISecondaryIndexProcessor {
	private readonly MemoryCache _streamIdCache = new(new MemoryCacheOptions());
	private readonly MemoryCacheEntryOptions _options = new() { SlidingExpiration = TimeSpan.FromMinutes(10) };
	private long _lastLogPosition;
	private readonly Appender _appender = new(connection, "streams"u8);
	public long Seq { get; private set; } = connection.QueryFirstOrDefault<long, QueryStreamsMaxSequencesSql>() ?? 0;
	public long LastCommittedPosition { get; private set; }
	public long LastIndexed { get; private set; }

	public ValueTask Index(ResolvedEvent resolvedEvent, CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return ValueTask.CompletedTask;

		var name = resolvedEvent.OriginalStreamId;
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		if (_streamIdCache.TryGetValue(name, out var existing)) {
			LastIndexed = (long)existing!;
			return ValueTask.CompletedTask;
		}

		var fromDb = connection.QueryFirstOrDefault<QueryStreamArgs, long, QueryStreamIdSql>(
			new QueryStreamArgs { StreamName = name }
		);
		if (fromDb.HasValue) {
			_streamIdCache.Set(name, fromDb, _options);
			LastIndexed = fromDb.Value;
			return ValueTask.CompletedTask;
		}

		var id = ++Seq;
		_streamIdCache.Set(name, id, _options);

		using var row = _appender.CreateRow();
		row.Append(id);
		row.Append(name);
		row.AppendDefault();
		row.AppendDefault();

		LastIndexed = id;
		return ValueTask.CompletedTask;
	}

	public ValueTask Commit(CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return ValueTask.CompletedTask;

		_appender.Flush();
		LastCommittedPosition = _lastLogPosition;

		return ValueTask.CompletedTask;
	}
}
