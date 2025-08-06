// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Readers;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexProcessor {
	private readonly Dictionary<string, int> _categories;
	private readonly Dictionary<int, long> _categorySizes = new();
	private readonly DuckDbDataSource _db;
	private readonly IPublisher _publisher;
	private readonly IQueryTracker _queryTracker;

	private int _seq;
	public long LastIndexedPosition { get; private set; }

	public CategoryIndexProcessor(DuckDbDataSource db, IPublisher publisher, IQueryTracker queryTracker) {
		_db = db;
		_publisher = publisher;
		_queryTracker = queryTracker;

		var ids = db.Pool.Query<ReferenceRecord, GetCategoriesQuery>(queryTracker.DontTrack);
		var sequences = db.Pool.Query<(int Id, long Sequence), GetCategoriesMaxSequencesQuery>(queryTracker.DontTrack)
			.ToDictionary(ks => ks.Id, vs => vs.Sequence);

		_categories = ids.ToDictionary(x => x.Name, x => x.Id);
		_categorySizes = ids.ToDictionary(x => x.Id, x => sequences.GetValueOrDefault(x.Id, -1L));

		_seq = _categories.Count > 0 ? _categories.Values.Max() - 1 : -1;
	}

	public SequenceRecord Index(ResolvedEvent resolvedEvent) {
		var categoryName = GetStreamCategory(resolvedEvent.OriginalStreamId);

		if (!_categories.TryGetValue(categoryName, out var categoryId)) {
			categoryId = ++_seq;
			_categories[categoryName] = categoryId;
			_db.Pool.ExecuteNonQuery<AddCategoryStatementArgs, AddCategoryStatement>(new(categoryId, categoryName), _queryTracker);
		}

		var nextCategorySequence = _categorySizes.GetValueOrDefault(categoryId, -1) + 1;
		_categorySizes[categoryId] = nextCategorySequence;

		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(
				resolvedEvent.ToResolvedLink(CategoryIndex.Name(categoryName), nextCategorySequence))
		);

		return new SequenceRecord(categoryId, nextCategorySequence);
	}

	public long GetLastEventNumber(int categoryId) =>
		_categorySizes.TryGetValue(categoryId, out var size) ? size : ExpectedVersion.NoStream;

	public int GetCategoryId(string categoryName) =>
		_categories.TryGetValue(categoryName, out var categoryId) ? categoryId : -1;

	private static string GetStreamCategory(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}
}
