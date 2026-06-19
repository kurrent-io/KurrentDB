// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.Arrow;
using KurrentDB.Core.DuckDB;
using QuackQueryResult = Kurrent.Quack.QueryResult;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private sealed class QueryResultReader : Disposable, IQueryResultReader {
		private readonly DuckDBCpuMetrics _cpuMetrics;
		private QuackQueryResult _result;
		private DataChunk _chunk;

		public QueryResultReader(in PreparedStatement statement, bool useStreaming, DuckDBCpuMetrics cpuMetrics) {
			_cpuMetrics = cpuMetrics;
			_result = statement.ExecuteQuery(useStreaming);
		}

		internal void ThrowOnError() => _result.ThrowOnError();

		public ArrowOptions GetArrowOptions() => _result.GetArrowOptions();

		public Schema GetArrowSchema(ArrowOptions options) => _result.GetArrowSchema();

		public bool TryRead() {
			_chunk.Dispose();
			// In streaming mode this fetch is where DuckDB produces the next result chunk,
			// so its CPU is attributed to the query here rather than in QueryEngine.ExecuteAsync.
			using var cpu = _cpuMetrics.Measure(DuckDBCpuMetrics.Activities.Query);
			return _result.TryFetch(out _chunk);
		}

		public ref readonly DataChunk Chunk => ref _chunk;

		private void FinalizeEnumeration() {
			using var cpu = _cpuMetrics.Measure(DuckDBCpuMetrics.Activities.Query);
			while (_result.TryFetch(out _chunk)) {
				_chunk.Dispose();
			}
		}

		protected override void Dispose(bool disposing) {
			FinalizeEnumeration();
			_result.Dispose();
			base.Dispose(disposing);
		}
	}
}
