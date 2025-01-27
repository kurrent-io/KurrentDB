using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dapper;
using EventStore.Core.Data;
using EventStore.Core.Metrics;
using Eventuous.Subscriptions.Context;

namespace EventStore.Core.Duck.Default;

class CategoryIndex(DuckDb db) {
	internal Dictionary<string, long> Categories = new();
	readonly Dictionary<long, long> CategorySizes = new();

	public void Init() {
		var ids = db.Connection.Query<ReferenceRecord>("select * from category").ToList();
		Categories = ids.ToDictionary(x => x.name, x => x.id);
		foreach (var id in ids) {
			CategorySizes[id.id] = -1;
		}

		const string query = "select category, max(category_seq) from idx_all group by category";
		var sequences = db.Connection.Query<(long Id, long Sequence)>(query);
		foreach (var sequence in sequences) {
			CategorySizes[sequence.Id] = sequence.Sequence;
		}

		Seq = Categories.Count > 0 ? Categories.Values.Max() : 0;
	}

	public IEnumerable<IndexedPrepare> GetRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = QueryCategory(id, fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.category_seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	List<CategoryRecord> QueryCategory(long id, long fromEventNumber, long toEventNumber) {
		const string query = """
		                     select category_seq, log_position, event_number
		                     from idx_all where category=$cat and category_seq>=$start and category_seq<=$end
		                     """;

		using var duration = TempIndexMetrics.MeasureIndex("duck_get_cat_range");
		var result = db.Connection.QueryWithRetry<CategoryRecord>(query, new { cat = id, start = fromEventNumber, end = toEventNumber }).ToList();
		return result;
	}

	public long GetLastEventNumber(long categoryId) => CategorySizes.TryGetValue(categoryId, out var size) ? size : ExpectedVersion.NoStream;

	long GetCategoryLastEventNumber(long categoryId) {
		return db.Connection.QueryWithRetry<long>("select max(seq) from idx_all where category=$cat", new { cat = categoryId }).SingleOrDefault();
	}

	static string GetStreamCategory(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? streamName : streamName[..dashIndex];
	}

	string GetCategoryName(string streamName) {
		var dashIndex = streamName.IndexOf('-');
		return dashIndex == -1 ? throw new InvalidOperationException($"Stream {streamName} is not a category stream") : streamName[(dashIndex + 1)..];
	}

	public SequenceRecord Handle(IMessageConsumeContext ctx) {
		var categoryName = GetStreamCategory(ctx.Stream.ToString());
		LastPosition = (long)ctx.GlobalPosition;
		if (Categories.TryGetValue(categoryName, out var val)) {
			var next = CategorySizes[val] + 1;
			CategorySizes[val] = next;
			return new(val, next);
		}

		var id = ++Seq;
		db.Connection.ExecuteWithRetry(CatSql, new { id, name = categoryName });
		Categories[categoryName] = id;
		CategorySizes[id] = 0;
		return new(id, 0);
	}

	internal long LastPosition { get; private set; }

	static long Seq;
	static readonly string CatSql = Sql.AppendIndexSql.Replace("{table}", "category");
}
