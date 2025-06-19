// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using static KurrentDB.SecondaryIndexing.Indexes.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndexProcessor : Disposable {
	static readonly ILogger Log = Serilog.Log.ForContext<StreamIndexProcessor>();

	readonly IIndexBackend<string> _indexReaderBackend;
	readonly ILongHasher<string> _hasher;
	readonly DuckDBAdvancedConnection _connection;
	readonly Dictionary<string, long> _inFlightRecords = new();

	long _lastLogPosition;
	int _count;
	long _seq;
	Appender _appender;

	public StreamIndexProcessor(DuckDbDataSource db, IIndexBackend<string> indexReaderBackend, ILongHasher<string> hasher) {
		_indexReaderBackend = indexReaderBackend;
		_hasher = hasher;
		_connection = db.OpenNewConnection();
		_appender = new Appender(_connection, "streams"u8);
		_seq = _connection.QueryFirstOrDefault<Optional<long>, GetStreamMaxSequencesQuery>().WithDefault(-1);
	}

	public long LastCommittedPosition { get; private set; }

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
			_indexReaderBackend.UpdateStreamSecondaryIndexId(1, name, fromDb.Value);
			return fromDb.Value;
		}

		id = ++_seq;
		_indexReaderBackend.UpdateStreamSecondaryIndexId(1, name, id);

		_inFlightRecords.Add(name, id);

		var streamHash = _hasher.Hash(name);
		using (var row = _appender.CreateRow()) {
			row.Append(id);
			row.Append(name);
			row.Append(streamHash);
			row.AppendDefault();
			row.AppendDefault();
		}

		_count++;

		return id;
	}

	private readonly Stopwatch _stopwatch = new();

	public void Commit() {
		if (IsDisposed || _count == 0)
			return;

		_inFlightRecords.Clear();
		_stopwatch.Start();
		_appender.Flush();
		_stopwatch.Stop();
		Log.Debug("Committed {Count} records to streams at seq {Seq} ({Took} ms)", _count, _seq, _stopwatch.ElapsedMilliseconds);
		_stopwatch.Reset();

		LastCommittedPosition = _lastLogPosition;
		_count = 0;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
