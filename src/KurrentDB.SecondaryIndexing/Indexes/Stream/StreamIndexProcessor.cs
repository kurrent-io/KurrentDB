// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using static KurrentDB.SecondaryIndexing.Indexes.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndexProcessor : Disposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<StreamIndexProcessor>();

	private readonly IIndexBackend<string> _streamsCache;
	private readonly ILongHasher<string> _hasher;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly Dictionary<string, long> _inFlightRecords = new();

	private long _lastLogPosition;
	private int _count;
	private long _seq;
	private Appender _appender;

	public StreamIndexProcessor(DuckDbDataSource db, IIndexBackend<string> streamsCache, ILongHasher<string> hasher) {
		_streamsCache = streamsCache;
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

		if (_streamsCache.TryGetStreamLastEventNumber(name) is { SecondaryIndexId: { } secondaryIndexId }) {
			return secondaryIndexId;
		}

		var fromDb =
			_connection.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, long, GetStreamIdByNameQuery>(new(name));
		if (fromDb.HasValue) {
			_streamsCache.UpdateStreamSecondaryIndexId(1, name, fromDb.Value);
			return fromDb.Value;
		}

		id = ++_seq;
		_streamsCache.UpdateStreamSecondaryIndexId(1, name, id);

		_inFlightRecords.Add(name, id);

		var streamHash = _hasher.Hash(name);
		using (var row = _appender.CreateRow()) {
			row.Append(id);
			row.Append(name);
			row.Append(streamHash);
			row.AppendDefault(); // max_age
			row.AppendDefault(); // max_count
			row.AppendDefault(); // is_deleted
			row.AppendDefault(); // acl
		}

		_count++;

		return id;
	}

	public void HandleStreamMetadataChange(ResolvedEvent evt) {
		var metadata = StreamMetadata.FromJson(Helper.UTF8NoBom.GetString(evt.Event.Data.Span));
		_connection.UpdateStreamMetadata(
			new UpdateStreamMetadataParams(
				metadata.MaxAge,
				metadata.MaxCount,
				metadata.TruncateBefore,
				metadata.Acl
			)
		);
	}

	private readonly Stopwatch _stopwatch = new();

	public void Commit() {
		if (IsDisposed || _count == 0)
			return;

		_inFlightRecords.Clear();
		_stopwatch.Start();
		_appender.Flush();
		_stopwatch.Stop();
		Log.Debug("Committed {Count} records to streams at seq {Seq} ({Took} ms)", _count, _seq,
			_stopwatch.ElapsedMilliseconds);
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
