// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Duck.Default;

class DefaultIndexReader(DuckDbDataSource db, DefaultIndexHandler handler, IReadIndex<string> index) : DuckIndexReader(index) {
	protected override long GetId(string streamName) => 0;

	protected override long GetLastNumber(long id) => handler.LastSequence - 1;

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long _, long fromEventNumber, long toEventNumber) {
		var range = QueryAll(fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	public override ValueTask<long> GetLastIndexedPosition() => ValueTask.FromResult(handler.LastPosition);

	public override bool OwnStream(string streamId) => streamId == "$idx-all";

	List<AllRecord> QueryAll(long fromEventNumber, long toEventNumber) {
		const string query = "select seq, log_position, event_number from idx_all where seq>=$start and seq<=$end";

		using var duration = TempIndexMetrics.MeasureIndex("duck_get_all_range");
		using var connection = db.Pool.Open();
		return connection.Query<AllRecord>(query, new { start = fromEventNumber, end = toEventNumber }).ToList();
	}
}
