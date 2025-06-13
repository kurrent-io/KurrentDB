// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using DotNext;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

class CategoryIndex {
	public const string IndexPrefix = $"{SystemStreams.IndexStreamPrefix}ce-";

	public CategoryIndex(
		DuckDbDataSource db,
		IReadIndex<string> readIndex,
		QueryInFlightRecords<CategorySql.CategoryRecord> queryInFlightRecords
	) {
		Processor = new(db);
		Reader = new CategoryIndexReader(db, Processor, readIndex, queryInFlightRecords);
	}

	public CategoryIndexProcessor Processor { get; }

	public IVirtualStreamReader Reader { get; }
}
