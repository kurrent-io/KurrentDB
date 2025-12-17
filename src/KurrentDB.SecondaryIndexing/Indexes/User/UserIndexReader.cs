// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal class UserIndexReader<TField>(
	DuckDBConnectionPool sharedPool,
	UserIndexSql<TField> sql,
	UserIndexInFlightRecords<TField> inFlightRecords,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(sharedPool, index) where TField : IField {

	protected override string? GetId(string indexStream) {
		// the field is used as the ID. null when there is no field
		// it is only used for passing into the overrides defined in this class
		UserIndexHelpers.ParseQueryStreamName(indexStream, out _, out var field);
		return field;
	}

	protected override (List<IndexQueryRecord> Records, bool IsFinal) GetInflightForwards(string? id, long startPosition, int maxCount, bool excludeFirst) {
		if (!TryGetField(id, out var field))
			return ([], true);

		return inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, Filter);
		bool Filter(UserIndexInFlightRecord<TField> r) => id is null || EqualityComparer<TField>.Default.Equals(r.Field, field!);
	}

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db, string? id, long startPosition, long endPosition, int maxCount, bool excludeFirst) {
		if (!TryGetField(id, out var field))
			return [];

		var args = new ReadUserIndexQueryArgs {
			StartPosition = startPosition,
			EndPosition = endPosition,
			ExcludeFirst = excludeFirst,
			Count = maxCount,
			Field = id is null ? new NullField() : field!
		};

		return sql.ReadUserIndexForwardsQuery(db, args);
	}

	protected override IEnumerable<IndexQueryRecord> GetInflightBackwards(string? id, long startPosition, int maxCount, bool excludeFirst) {
		if (!TryGetField(id, out var field))
			return [];

		return inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, Filter);
		bool Filter(UserIndexInFlightRecord<TField> r) => id is null || EqualityComparer<TField>.Default.Equals(r.Field, field!);
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db, string? id, long startPosition, int maxCount, bool excludeFirst) {
		if (!TryGetField(id, out var field))
			return [];

		var args = new ReadUserIndexQueryArgs {
			StartPosition = startPosition,
			Count = maxCount,
			ExcludeFirst = excludeFirst,
			Field = id is null ? new NullField() : field!
		};

		return sql.ReadUserIndexBackwardsQuery(db, args);
	}

	public override TFPos GetLastIndexedPosition(string _) => throw new InvalidOperationException(); // never called
	public override bool CanReadIndex(string _) => throw new InvalidOperationException(); // never called

	private static bool TryGetField(string? id, out TField? field) {
		field = default;

		if (id is null)
			return true;

		try {
			field = (TField) TField.ParseFrom(id);
			return true;
		} catch {
			// invalid field
			return false;
		}
	}
}
