// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using static KurrentDB.SecondaryIndexing.Indexes.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndexProcessor<TStreamId> : Disposable {
	static readonly ILogger Log = Serilog.Log.ForContext<StreamIndexProcessor<TStreamId>>();

	readonly Appender _appender;
	readonly DuckDbDataSource _db;
	readonly IIndexBackend<TStreamId> _indexReaderBackend;
	readonly DuckDBAdvancedConnection _connection;
	readonly Dictionary<string, ulong> _inFlightRecords = new();

	long _lastLogPosition;

	public StreamIndexProcessor(DuckDbDataSource db, IIndexBackend<TStreamId> indexReaderBackend) {
		_db = db;
		_indexReaderBackend = indexReaderBackend;
		_connection = _db.OpenNewConnection();
		_appender = new(_connection, "streams"u8);
		Seq = _connection.QueryFirstOrDefault<ulong, GetStreamMaxSequencesQuery>() ?? 0;
	}

	public ulong Seq { get; private set; }
	public long LastCommittedPosition { get; private set; }

	int _count;

	public long Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return -1;

		string name = resolvedEvent.OriginalStreamId;
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		// TODO: meh, think if we can drop constraint or at least use something like IParsable
		var streamId = Unsafe.As<string, TStreamId>(ref name);

		if (_inFlightRecords.TryGetValue(name, out var id))
			return (long)id;

		var cached = _indexReaderBackend.TryGetStreamLastEventNumber(streamId);

		if (cached.SecondaryIndexId.HasValue) {
			return (long)cached.SecondaryIndexId.Value;
		}

		var fromDb = _connection.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, ulong, GetStreamIdByNameQuery>(new(name));
		if (fromDb.HasValue) {
			_indexReaderBackend.UpdateStreamSecondaryIndexId(1, streamId, fromDb.GetValueOrDefault());
			return (long)fromDb.GetValueOrDefault();
		}

		Log.Information("Stream {Name} not in the db", streamId);
		id = ++Seq;
		_indexReaderBackend.UpdateStreamSecondaryIndexId(1, streamId, id);

		_inFlightRecords[name] = id;

		using (var row = _appender.CreateRow()) {
			row.Append(id);
			row.Append(name);
			row.AppendDefault();
			row.AppendDefault();
		}

		_count++;

		return (long)id;
	}

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		Log.Information("In-flight records count: {Count}", _inFlightRecords.Count);
		Log.Information("Appender rows count: {Count}", _count);
		_inFlightRecords.Clear();
		_count = 0;

		_appender.Flush();
		LastCommittedPosition = _lastLogPosition;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
