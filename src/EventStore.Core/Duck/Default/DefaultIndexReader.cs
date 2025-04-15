// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Dapper;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using Serilog;

namespace EventStore.Core.Duck.Default;

class DefaultIndexReader<TStreamId>(DuckDb db, DefaultIndexHandler<TStreamId> handler, IReadIndex<TStreamId> index) : DuckIndexReader<TStreamId>(index) {
	static readonly ILogger Log = Serilog.Log.ForContext<DefaultIndexReader<TStreamId>>();
	protected override long GetId(string streamName) => 0;

	protected override long GetLastNumber(long id) => handler.LastSequence - 1;

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long _, long fromEventNumber, long toEventNumber) {
		// Log.Debug("Querying default index from {From} to {To}", fromEventNumber, toEventNumber);
		var range = QueryAll(fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	public override ValueTask<long> GetLastIndexedPosition() => ValueTask.FromResult(handler.LastPosition);

	public override bool OwnStream(string streamId) => streamId == "$everything";

	// [MethodImpl(MethodImplOptions.Synchronized)]
	List<AllRecord> QueryAll(long fromEventNumber, long toEventNumber) {
		const string query = "select seq, log_position, event_number from idx_all where seq>=$start and seq<=$end";

		var duration = TempIndexMetrics.MeasureIndex("duck_get_all_range");
		var connection = db.GetOrOpenConnection();
		try {
			return connection
				.Query<AllRecord>(query, new { start = fromEventNumber, end = toEventNumber }).ToList();
		} finally {
			duration.Dispose();
			db.ReturnConnection(connection);
		}
	}
}
