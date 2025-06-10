// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Linq;
using Dapper;
using DotNext;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Duck.Default;

public class DefaultIndex<TStreamId> : Disposable {
	readonly DuckDb _db;

	public DefaultIndex(DuckDb db, IReadIndex<TStreamId> index) {
		_db = db;
		StreamIndex = new(db);
		CategoryIndex = new(db);
		EventTypeIndex = new(db);
		CategoryIndexReader = new(CategoryIndex, index);
		EventTypeIndexReader = new(EventTypeIndex, index);
		Handler = new(db, this);
		DefaultIndexReader = new(_db, Handler, index);
	}

	public void Init() {
		CategoryIndex.Init();
		EventTypeIndex.Init();
	}

	public ulong? GetLastPosition() {
		const string query = "select max(log_position) from idx_all";
		using var connection = _db.GetOrOpenConnection();
		return connection.Query<ulong?>(query).FirstOrDefault();
	}

	public ulong? GetLastSequence() {
		const string query = "select max(seq) from idx_all";
		using var connection = _db.GetOrOpenConnection();
		return connection.Query<ulong?>(query).FirstOrDefault();
	}

	internal StreamIndex StreamIndex;
	internal CategoryIndex CategoryIndex;
	internal EventTypeIndex EventTypeIndex;

	internal readonly CategoryIndexReader<TStreamId> CategoryIndexReader;
	internal readonly EventTypeIndexReader<TStreamId> EventTypeIndexReader;
	internal DefaultIndexReader<TStreamId> DefaultIndexReader;
	internal DefaultIndexHandler<TStreamId> Handler;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			StreamIndex.Dispose();
		}

		base.Dispose(disposing);
	}
}

public record struct SequenceRecord(long Id, long Sequence);
