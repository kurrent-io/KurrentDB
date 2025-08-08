// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

public class CategoryIndexProcessor {
	private readonly Dictionary<string, int> _categories;
	private readonly DuckDbDataSource _db;
	private readonly IPublisher _publisher;

	private int _seq;
	public long LastIndexedPosition { get; private set; }

	public CategoryIndexProcessor(DuckDbDataSource db, IPublisher publisher) {
		_db = db;
		_publisher = publisher;

		var ids = db.Pool.Query<ReferenceRecord, GetCategoriesQuery>();

		_categories = ids.ToDictionary(x => x.Name, x => x.Id);
		_seq = _categories.Count > 0 ? _categories.Values.Max() - 1 : -1;
	}

	public int Index(ResolvedEvent resolvedEvent) {
		var categoryName = GetStreamCategory(resolvedEvent.OriginalStreamId);

		if (!_categories.TryGetValue(categoryName, out var categoryId)) {
			categoryId = ++_seq;
			_categories[categoryName] = categoryId;
			_db.Pool.ExecuteNonQuery<AddCategoryStatementArgs, AddCategoryStatement>(new(categoryId, categoryName));
		}

		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(CategoryIndex.Name(categoryName), resolvedEvent));

		return categoryId;
	}

	public int GetCategoryId(string categoryName) =>
		_categories.TryGetValue(categoryName, out var categoryId) ? categoryId : -1;

	private static string GetStreamCategory(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}
}
