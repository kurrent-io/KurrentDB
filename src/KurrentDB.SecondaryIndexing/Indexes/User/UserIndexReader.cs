// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Quack.Threading;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal sealed class UserIndexReader(
	DuckDBConnectionPool sharedPool,
	UserIndexProcessor processor,
	IReadOnlyList<IField> fields,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(sharedPool, index) {

	// the raw field-constraint suffix is used as the id; it is parsed against the index's fields in the overrides below
	protected override string? GetId(string indexStream) {
		UserIndexHelpers.ParseQueryStreamName(indexStream, out _, out var suffix);
		return suffix?.ToString() ?? null;
	}

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db, string? id, long startPosition, int maxCount, bool excludeFirst) {
		var suffix = id is null ? (ReadOnlyMemory<char>?)null : id.AsMemory();
		if (!UserIndexHelpers.TryParseConstraints(fields, suffix, out var constraints))
			throw new InvalidOperationException($"Failed to parse already-validated field constraints for index read (id: '{id}').");

		var args = new ReadUserIndexQueryArgs {
			StartPosition = startPosition,
			ExcludeFirst = excludeFirst,
			Count = maxCount,
			Constraints = constraints
		};

		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection))
		using (processor.CaptureSnapshot(connection)) {
			processor.Sql.ReadUserIndexForwardsQuery(connection, args, records);
		}

		return records;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db, string? id, long startPosition, int maxCount, bool excludeFirst) {
		var suffix = id is null ? (ReadOnlyMemory<char>?)null : id.AsMemory();
		if (!UserIndexHelpers.TryParseConstraints(fields, suffix, out var constraints))
			throw new InvalidOperationException($"Failed to parse already-validated field constraints for index read (id: '{id}').");

		var args = new ReadUserIndexQueryArgs {
			StartPosition = startPosition,
			ExcludeFirst = excludeFirst,
			Count = maxCount,
			Constraints = constraints
		};

		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection))
		using (processor.CaptureSnapshot(connection)) {
			processor.Sql.ReadUserIndexBackwardsQuery(connection, args, records);
		}

		return records;
	}

	public override TFPos GetLastIndexedPosition(string _) => throw new InvalidOperationException(); // never called
	public override bool CanReadIndex(string _) => throw new InvalidOperationException(); // never called

	internal BufferedView.Snapshot CaptureSnapshot(DuckDBAdvancedConnection connection)
		=> processor.CaptureSnapshot(connection);
}
