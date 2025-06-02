// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexProcessor : ISecondaryIndexProcessor {
	readonly Dictionary<string, long> _categories;
	readonly Dictionary<long, long> _categorySizes = new();
	readonly DuckDbDataSource _db;

	long _lastLogPosition;

	public long Seq { get; private set; }
	public long LastCommittedPosition { get; private set; }

	public CategoryIndexProcessor(DuckDbDataSource db) {
		_db = db;
		var ids = db.Pool.Query<ReferenceRecord, GetCategoriesQuery>();
		_categories = ids.ToDictionary(x => x.Name, x => x.Id);

		foreach (var id in ids) {
			_categorySizes[id.Id] = -1;
		}

		var sequences = db.Pool.Query<(long Id, long Sequence), GetCategoriesMaxSequencesQuery>();
		foreach (var sequence in sequences) {
			_categorySizes[sequence.Id] = sequence.Sequence;
		}

		Seq = _categories.Count > 0 ? _categories.Values.Max() : 0;
	}

	public SequenceRecord Index(ResolvedEvent resolvedEvent) {
		var categoryName = GetStreamCategory(resolvedEvent.OriginalStreamId);
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		if (_categories.TryGetValue(categoryName, out var categoryId)) {
			var next = _categorySizes[categoryId] + 1;
			_categorySizes[categoryId] = next;
			return new(categoryId, next);
		}

		var id = ++Seq;

		_categories[categoryName] = id;
		_categorySizes[id] = 0;

		_db.Pool.ExecuteNonQuery<AddCategoryStatementArgs, AddCategoryStatement>(new((int)id, categoryName));
		_lastLogPosition = resolvedEvent.Event.LogPosition;
		return new(id, 0);
	}

	public long GetLastEventNumber(long categoryId) =>
		_categorySizes.TryGetValue(categoryId, out var size) ? size : ExpectedVersion.NoStream;

	public long GetCategoryId(string categoryName) =>
		_categories.TryGetValue(categoryName, out var categoryId) ? categoryId : ExpectedVersion.NoStream;

	public void Commit() {
		LastCommittedPosition = _lastLogPosition;
	}

	private static string GetStreamCategory(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}
}
