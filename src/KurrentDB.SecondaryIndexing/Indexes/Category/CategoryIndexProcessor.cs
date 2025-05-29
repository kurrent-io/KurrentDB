// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly Dictionary<string, long> _categories;
	private readonly Dictionary<long, long> _categorySizes = new();
	private long _lastLogPosition;
	private readonly Appender _appender;

	public long Seq { get; private set; }
	public long LastCommittedPosition { get; private set; }
	public SequenceRecord LastIndexed { get; private set; }

	public CategoryIndexProcessor(DuckDBAdvancedConnection connection) {
		_appender = new(connection, "category"u8);

		var ids = connection.Query<ReferenceRecord, QueryCategorySql>();
		_categories = ids.ToDictionary(x => x.name, x => x.id);

		foreach (var id in ids) {
			_categorySizes[id.id] = -1;
		}

		var sequences = connection.Query<(long Id, long Sequence), QueryCategoriesMaxSequencesSql>();
		foreach (var sequence in sequences) {
			_categorySizes[sequence.Id] = sequence.Sequence;
		}

		Seq = _categories.Count > 0 ? _categories.Values.Max() : 0;
	}

	public void Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return;

		var categoryName = GetStreamCategory(resolvedEvent.OriginalStreamId);
		_lastLogPosition = resolvedEvent.Event.LogPosition;

		if (_categories.TryGetValue(categoryName, out var categoryId)) {
			var next = _categorySizes[categoryId] + 1;
			_categorySizes[categoryId] = next;

			LastIndexed = new(categoryId, next);
			return;
		}

		var id = ++Seq;

		_categories[categoryName] = id;
		_categorySizes[id] = 0;

		using (var row = _appender.CreateRow()) {
			row.Append(id);
			row.Append(categoryName);
		}

		_lastLogPosition = resolvedEvent.Event.LogPosition;
		LastIndexed = new(id, 0);
	}

	public long GetLastEventNumber(long categoryId) =>
		_categorySizes.TryGetValue(categoryId, out var size) ? size : ExpectedVersion.NoStream;

	public long GetCategoryId(string categoryName) =>
		_categories.TryGetValue(categoryName, out var categoryId) ? categoryId : ExpectedVersion.NoStream;

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		_appender.Flush();
		LastCommittedPosition = _lastLogPosition;
	}

	private static string GetStreamCategory(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}
}
