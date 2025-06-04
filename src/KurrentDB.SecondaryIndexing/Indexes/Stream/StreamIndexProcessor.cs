// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using static KurrentDB.SecondaryIndexing.Indexes.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndexProcessor : Disposable {
	static readonly ILogger Log = Serilog.Log.ForContext<StreamIndexProcessor>();

	readonly Appender _appender;
	readonly IIndexBackend<string> _indexReaderBackend;
	readonly DuckDBAdvancedConnection _connection;
	readonly Dictionary<string, long> _inFlightRecords = new();

	long _lastLogPosition;

	public StreamIndexProcessor(DuckDbDataSource db, IIndexBackend<string> indexReaderBackend) {
		_indexReaderBackend = indexReaderBackend;
		_connection = db.OpenNewConnection();
		_appender = new(_connection, "streams"u8);
		Seq = _connection.QueryFirstOrDefault<long, GetStreamMaxSequencesQuery>() ?? 0;
	}

	public long Seq { get; private set; }
	public long LastCommittedPosition { get; private set; }

	int _count;

	public long Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return -1;

		string name = resolvedEvent.OriginalStreamId;
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		if (_inFlightRecords.TryGetValue(name, out var id))
			return id;

		if (_indexReaderBackend.TryGetStreamLastEventNumber(name) is { SecondaryIndexId: { } secondaryIndexId }) {
			return secondaryIndexId;
		}

		var fromDb = _connection.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, long, GetStreamIdByNameQuery>(new(name));
		if (fromDb.HasValue) {
			_indexReaderBackend.UpdateStreamSecondaryIndexId(1, name, fromDb.GetValueOrDefault());
			return fromDb.GetValueOrDefault();
		}

		id = ++Seq;
		_indexReaderBackend.UpdateStreamSecondaryIndexId(1, name, id);

		_inFlightRecords.Add(name, id);

		using (var row = _appender.CreateRow()) {
			row.Append(id);
			row.Append(name);
			row.AppendDefault();
			row.AppendDefault();
		}

		_count++;

		return id;
	}

	public void Commit() {
		if (IsDisposingOrDisposed || _count == 0)
			return;

		_inFlightRecords.Clear();
		_appender.Flush();
		LastCommittedPosition = _lastLogPosition;
		_count = 0;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
