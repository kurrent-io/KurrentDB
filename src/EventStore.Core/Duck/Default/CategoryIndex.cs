// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using Dapper;
using EventStore.Core.Data;
using EventStore.Core.Metrics;
using Eventuous.Subscriptions.Context;

namespace EventStore.Core.Duck.Default;

class CategoryIndex(DuckDbDataSource db) {
	internal Dictionary<string, long> _categories = new();
	readonly Dictionary<long, long> _categorySizes = new();

	public void Init() {
		using var connection = db.OpenConnection();
		var ids = connection.Query<ReferenceRecord>("select * from category").ToList();

		_categories = ids.ToDictionary(x => x.name, x => x.id);
		foreach (var id in ids) {
			_categorySizes[id.id] = -1;
		}

		const string query = "select category, max(category_seq) from idx_all group by category";

		var sequences = connection.Query<(long Id, long Sequence)>(query);
		foreach (var sequence in sequences) {
			_categorySizes[sequence.Id] = sequence.Sequence;
		}

		Seq = _categories.Count > 0 ? _categories.Values.Max() : 0;
	}

	public IEnumerable<IndexedPrepare> GetRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = QueryCategory(id, fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.category_seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	List<CategoryRecord> QueryCategory(long id, long fromEventNumber, long toEventNumber) {
		const string query = """
		                     select category_seq, log_position, event_number
		                     from idx_all where category=$cat and category_seq>=$start and category_seq<=$end
		                     """;

		using var duration = TempIndexMetrics.MeasureIndex("duck_get_cat_range");
		using var connection = db.Pool.Open();
		return connection
			.Query<CategoryRecord>(query, new { cat = id, start = fromEventNumber, end = toEventNumber })
			.ToList();
	}

	public long GetLastEventNumber(long categoryId) => _categorySizes.TryGetValue(categoryId, out var size) ? size : ExpectedVersion.NoStream;

	static string GetStreamCategory(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}

	public SequenceRecord Handle(IMessageConsumeContext ctx) {
		var categoryName = GetStreamCategory(ctx.Stream.ToString());
		LastPosition = (long)ctx.GlobalPosition;
		if (_categories.TryGetValue(categoryName, out var val)) {
			var next = _categorySizes[val] + 1;
			_categorySizes[val] = next;
			return new(val, next);
		}

		var id = ++Seq;

		using var connection = db.Pool.Open();
		connection.Execute(CatSql, new { id, name = categoryName });
		_categories[categoryName] = id;
		_categorySizes[id] = 0;
		return new(id, 0);
	}

	internal long LastPosition { get; private set; }

	static long Seq;
	static readonly string CatSql = Sql.AppendIndexSql.Replace("{table}", "category");
}
