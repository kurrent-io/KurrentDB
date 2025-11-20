// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Custom.CustomSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal class CustomIndexReader<TPartitionKey>(
	string tableName,
	DuckDBConnectionPool db,
	IndexInFlightRecords inFlightRecords,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(db, index) where TPartitionKey : ITPartitionKey {

	protected override string GetId(string indexStream) {
		// the partition key is used as the ID
		CustomIndex.ParseStreamName(indexStream, out _, out var partitionKey);
		return partitionKey ?? string.Empty;
	}

	protected override (List<IndexQueryRecord> Records, bool IsFinal) GetInflightForwards(string id, long startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, Filter);
		bool Filter(InFlightRecord r) => id == string.Empty || r.PartitionKey == id;
	}

	protected override List<IndexQueryRecord> GetDbRecordsForwards(string id, long startPosition, long endPosition, int maxCount, bool excludeFirst) {
		var args = new ReadCustomIndexQueryArgs {
			StartPosition = startPosition,
			EndPosition = endPosition,
			Count = maxCount
		};

		if (!TryGetPartitionQuery(id, out var partitionQuery))
			return [];

		using (Db.Rent(out var connection)) {
			return excludeFirst ?
				connection.ReadCustomIndexQueryExcl(tableName, partitionQuery, args).ToList():
				connection.ReadCustomIndexQueryIncl(tableName, partitionQuery, args).ToList();
		}
	}

	protected override IEnumerable<IndexQueryRecord> GetInflightBackwards(string id, long startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, Filter);
		bool Filter(InFlightRecord r) => id == string.Empty || r.PartitionKey == id;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(string id, long startPosition, int maxCount, bool excludeFirst) {
		var args = new ReadCustomIndexQueryArgs {
			StartPosition = startPosition,
			Count = maxCount
		};

		if (!TryGetPartitionQuery(id, out var partitionQuery))
			return [];

		using (Db.Rent(out var connection)) {
			return excludeFirst ?
				connection.ReadCustomIndexBackQueryExcl(tableName, partitionQuery, args).ToList():
				connection.ReadCustomIndexBackQueryIncl(tableName, partitionQuery, args).ToList();
		}
	}

	public override TFPos GetLastIndexedPosition(string _) => throw new NotSupportedException(); // never called
	public override bool CanReadIndex(string _) => throw new NotSupportedException(); // never called

	private static bool TryGetPartitionQuery(string id, out string partitionQuery) {
		partitionQuery = string.Empty;
		if (id == string.Empty)
			return true;

		try {
			var partitionKey = TPartitionKey.ParseFrom(id);
			partitionQuery = $"and {partitionKey.GetDuckDbColumnQueryStatement()}";
			return true;
		} catch {
			// invalid partition key
			return false;
		}
	}
}
