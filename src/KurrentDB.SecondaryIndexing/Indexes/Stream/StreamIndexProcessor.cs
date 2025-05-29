// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Caching.Memory;
using static KurrentDB.SecondaryIndexing.Indexes.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndexProcessor<TStreamId>(
	DuckDBAdvancedConnection connection,
	IIndexBackend<TStreamId> indexReaderBackend
) : Disposable, ISecondaryIndexProcessor {
	private long _lastLogPosition;
	private readonly Appender _appender = new(connection, "streams"u8);

	public long Seq { get; private set; } = connection.QueryFirstOrDefault<long, QueryStreamsMaxSequencesSql>() ?? 0;
	public long LastCommittedPosition { get; private set; }
	public long LastIndexed { get; private set; }

	public void Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return;

		string name = resolvedEvent.OriginalStreamId;
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		// TODO: meh, think if we can drop constraint or at least use something like IParsable
		var streamId = Unsafe.As<string, TStreamId>(ref name);

		var cached = indexReaderBackend.TryGetStreamLastEventNumber(streamId);

		if (cached.SecondaryIndexId.HasValue) {
			LastIndexed = cached.SecondaryIndexId.Value;
			return;
		}

		var fromDb = connection.QueryFirstOrDefault<QueryStreamArgs, long, QueryStreamIdSql>(new() { StreamName = name });
		if (fromDb.HasValue) {
			indexReaderBackend.UpdateStreamSecondaryIndexId(1, streamId, fromDb.Value);
			LastIndexed = fromDb.Value;
			return;
		}

		var id = ++Seq;
		indexReaderBackend.UpdateStreamSecondaryIndexId(1, streamId, id);

		using var row = _appender.CreateRow();
		row.Append(id);
		row.Append(name);
		row.AppendDefault();
		row.AppendDefault();

		LastIndexed = id;
	}

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		_appender.Flush();
		LastCommittedPosition = _lastLogPosition;
	}
}
