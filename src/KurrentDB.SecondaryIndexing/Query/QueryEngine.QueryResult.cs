// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.Arrow;
using QuackQueryResult = Kurrent.Quack.QueryResult;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private sealed class QueryResultReader : Disposable, IQueryResultReader {
		private QuackQueryResult _result;
		private DataChunk _chunk;

		public QueryResultReader(in PreparedStatement statement, bool useStreaming) {
			_result = statement.ExecuteQuery(useStreaming);
		}

		public ArrowOptions GetArrowOptions() => _result.GetArrowOptions();

		public Schema GetArrowSchema(ArrowOptions options) => _result.GetArrowSchema();

		public bool TryRead() {
			_chunk.Dispose();
			return _result.TryFetch(out _chunk);
		}

		public ref readonly DataChunk Chunk => ref _chunk;

		private void FinalizeEnumeration() {
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
