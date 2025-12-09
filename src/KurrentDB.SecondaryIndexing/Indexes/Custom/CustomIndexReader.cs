// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal class CustomIndexReader<TField>(
	DuckDBConnectionPool sharedPool,
	CustomIndexSql<TField> sql,
	IndexInFlightRecords inFlightRecords,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(sharedPool, index) where TField : IField {

	protected override string? GetId(string indexStream) {
		// the partition is used as the ID
		CustomIndex.ParseStreamName(indexStream, out _, out var partition);
		return partition;
	}

	protected override (List<IndexQueryRecord> Records, bool IsFinal) GetInflightForwards(string? id, long startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, Filter);
		bool Filter(InFlightRecord r) => id is null || r.Partition == id;
	}

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db, string? id, long startPosition, long endPosition, int maxCount, bool excludeFirst) {
		if (!TryGetPartition(id, out var partition))
			return [];

		var args = new ReadCustomIndexQueryArgs {
			StartPosition = startPosition,
			EndPosition = endPosition,
			ExcludeFirst = excludeFirst,
			Count = maxCount,
			Partition = partition
		};

		return sql.ReadCustomIndexForwardsQuery(db, args);
	}

	protected override IEnumerable<IndexQueryRecord> GetInflightBackwards(string? id, long startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, Filter);
		bool Filter(InFlightRecord r) => id is null || r.Partition == id;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db, string? id, long startPosition, int maxCount, bool excludeFirst) {
		if (!TryGetPartition(id, out var partition))
			return [];

		var args = new ReadCustomIndexQueryArgs {
			StartPosition = startPosition,
			Count = maxCount,
			ExcludeFirst = excludeFirst,
			Partition = partition
		};

		return sql.ReadCustomIndexBackwardsQuery(db, args);
	}

	public override TFPos GetLastIndexedPosition(string _) => throw new NotSupportedException(); // never called
	public override bool CanReadIndex(string _) => throw new NotSupportedException(); // never called

	private static bool TryGetPartition(string? id, out IField partition) {
		partition = new NullField();

		if (id is null)
			return true;

		try {
			partition = TField.ParseFrom(id);
			return true;
		} catch {
			// invalid partition
			return false;
		}
	}
}
