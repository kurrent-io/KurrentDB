// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Readers;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexProcessor {
	private readonly Dictionary<string, int> _categories;
	private readonly Dictionary<int, long> _categorySizes = new();
	private readonly DuckDbDataSource _db;
	private readonly IPublisher _publisher;

	private int _seq;
	public long LastIndexedPosition { get; private set; }

	public CategoryIndexProcessor(DuckDbDataSource db, IPublisher publisher) {
		_db = db;
		_publisher = publisher;

		using var connection = db.OpenNewConnection();
		var ids = connection.Query<ReferenceRecord, GetCategoriesQuery>();
		_categories = ids.ToDictionary(x => x.Name, x => x.Id);

		foreach (var id in ids) {
			_categorySizes[id.Id] = -1;
		}

		var sequences = connection.Query<(int Id, long Sequence), GetCategoriesMaxSequencesQuery>();
		foreach (var sequence in sequences) {
			_categorySizes[sequence.Id] = sequence.Sequence;
		}

		_seq = _categories.Count > 0 ? _categories.Values.Max() - 1 : -1;
	}

	public SequenceRecord Index(ResolvedEvent resolvedEvent) {
		var categoryName = GetStreamCategory(resolvedEvent.OriginalStreamId);

		if (!_categories.TryGetValue(categoryName, out var categoryId)) {
			categoryId = ++_seq;
			_categories[categoryName] = categoryId;
			_db.Pool.ExecuteNonQuery<AddCategoryStatementArgs, AddCategoryStatement>(new(categoryId, categoryName));
		}

		var nextCategorySequence = _categorySizes.GetValueOrDefault(categoryId, -1) + 1;
		_categorySizes[categoryId] = nextCategorySequence;

		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(
				resolvedEvent.ToResolvedLink($"{CategoryIndex.IndexPrefix}{categoryName}", nextCategorySequence))
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
