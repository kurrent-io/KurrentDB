// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Logging;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

// ReSharper disable InvertIf

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexReader(
	DuckDBConnectionPool db,
	DefaultIndexProcessor processor,
	IReadIndex<string> index,
	DefaultIndexInFlightRecords inFlightRecords,
	ILogger<CategoryIndexReader> log)
	: SecondaryIndexReaderBase(db, index, log) {
	protected override string GetId(string indexName) =>
		CategoryIndex.TryParseCategoryName(indexName, out var categoryName)
			? categoryName
			: string.Empty;

	protected override (List<IndexQueryRecord>, bool) GetInflightForwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, r => r.Category == id);

	protected override List<IndexQueryRecord> GetDbRecordsForwards(string id, long startPosition, long endPosition, int maxCount, bool excludeFirst)
		=> excludeFirst
			? Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryExcl>(new(id, startPosition, endPosition, maxCount))
			: Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryIncl>(new(id, startPosition, endPosition, maxCount));

	protected override IEnumerable<IndexQueryRecord> GetInflightBackwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, r => r.Category == id);

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> excludeFirst
			? Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryExcl>(new(id, startPosition, 0, maxCount))
			: Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryIncl>(new(id, startPosition, 0, maxCount));

	public override TFPos GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => CategoryIndex.IsCategoryIndex(indexName);
}
