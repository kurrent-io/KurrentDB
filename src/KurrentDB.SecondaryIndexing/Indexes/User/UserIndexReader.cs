// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal class UserIndexReader<TField>(
	DuckDBConnectionPool sharedPool,
	UserIndexProcessor processor,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(sharedPool, index) where TField : IField<TField> {

	protected override string? GetId(string indexStream) {
		// the field is used as the ID. null when there is no field
		// it is only used for passing into the overrides defined in this class
		UserIndexHelpers.ParseQueryStreamName(indexStream, out _, out var field);
		return field;
	}

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db, string? id, long startPosition, int maxCount, bool excludeFirst) {
		if (!TryGetField(id, out var field))
			return [];

		var args = new ReadUserIndexQueryArgs {
			StartPosition = startPosition,
			ExcludeFirst = excludeFirst,
			Count = maxCount,
			Field = id is null ? NullField.Instance : field!
		};

		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection)) {
			using (processor.CaptureSnapshot(connection)) {
				processor.Sql.ReadUserIndexForwardsQuery(connection, args, records);
			}
		}

		return records;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		if (!TryGetField(id, out var field))
			return [];

		var args = new ReadUserIndexQueryArgs {
			StartPosition = startPosition,
			Count = maxCount,
			ExcludeFirst = excludeFirst,
			Field = id is null ? NullField.Instance : field!
		};

		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection)) {
			using (processor.CaptureSnapshot(connection)) {
				processor.Sql.ReadUserIndexBackwardsQuery(connection, args, records);
			}
		}

		return records;
	}

	public override TFPos GetLastIndexedPosition(string _) => throw new InvalidOperationException(); // never called
	public override bool CanReadIndex(string _) => throw new InvalidOperationException(); // never called

	private static bool TryGetField(string? id, out TField? field) {
		field = default;

		if (id is null)
			return true;

		try {
			field = TField.ParseFrom(id);
			return true;
		} catch {
			// invalid field
			return false;
		}
	}
}
