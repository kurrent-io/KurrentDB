// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexReader(
	DuckDBExecutor executor,
	DefaultIndexProcessor processor,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(executor, index) {
	protected override string GetId(string indexName) => string.Empty;

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBAdvancedConnection connection,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (processor.CaptureSnapshot(connection)) {
			if (excludeFirst) {
				connection.ExecuteQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryExcl>(new(
						startPosition,
						maxCount))
					.CopyTo(records);
			} else {
				connection.ExecuteQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryIncl>(new(
						startPosition,
						maxCount))
					.CopyTo(records);
			}
		}

		return records;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBAdvancedConnection connection,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (processor.CaptureSnapshot(connection)) {
			if (excludeFirst) {
				connection.ExecuteQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQueryExcl>(
					new(startPosition, maxCount))
					.CopyTo(records);
			} else {
				connection.ExecuteQuery<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQueryIncl>(
					new(startPosition, maxCount))
					.CopyTo(records);
			}
		}

		return records;
	}

	public override TFPos GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => indexName == SystemStreams.DefaultSecondaryIndex;
}
