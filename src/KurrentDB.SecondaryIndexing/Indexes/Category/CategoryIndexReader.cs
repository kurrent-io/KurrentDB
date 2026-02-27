// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

// ReSharper disable InvertIf

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexReader(
	DuckDBConnectionPool sharedPool,
	DefaultIndexProcessor processor,
	IReadIndex<string> index)
	: SecondaryIndexReaderBase(sharedPool, index) {
	protected override string GetId(string indexName) =>
		CategoryIndex.TryParseCategoryName(indexName, out var categoryName)
			? categoryName
			: string.Empty;

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection)) {
			using (processor.CaptureSnapshot(connection)) {
				if (excludeFirst) {
					connection.ExecuteQuery<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryExcl>(new(id!, startPosition,
							maxCount))
						.CopyTo(records);
				} else {
					connection.ExecuteQuery<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryIncl>(new(id!, startPosition,
						maxCount))
						.CopyTo(records);
				}
			}
		}

		return records;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection)) {
			using (processor.CaptureSnapshot(connection)) {
				if (excludeFirst) {
					connection.ExecuteQuery<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryExcl>(new(id!,
							startPosition, maxCount))
						.CopyTo(records);
				} else {
					connection.ExecuteQuery<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryIncl>(new(id!,
							startPosition, maxCount))
						.CopyTo(records);
				}
			}
		}

		return records;
	}

	public override TFPos GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => CategoryIndex.IsCategoryIndex(indexName);
}
