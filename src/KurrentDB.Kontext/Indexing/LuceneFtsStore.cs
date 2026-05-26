// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing;

public class LuceneFtsStore : IFtsStore {
	const string IdField = "id";

	readonly string _name;
	readonly StandardAnalyzer _analyzer;
	readonly IndexWriter _writer;
	readonly SearcherManager _searcherManager;
	readonly ILogger<LuceneFtsStore> _logger;
	IndexSearcher? _recoverySearcher;
	bool _dirty;

	public LuceneFtsStore(string ftsPath, string index, ILogger<LuceneFtsStore> logger) {
		_name = index;
		_logger = logger;
		_analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48, CharArraySet.Empty);

		var lucenePath = Path.Combine(ftsPath, $"{index}.lucene");
		var dir = FSDirectory.Open(new DirectoryInfo(lucenePath));
		var indexWriterConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer) {
			OpenMode = OpenMode.CREATE_OR_APPEND,
			Similarity = new BM25Similarity()
		};
		_writer = new IndexWriter(dir, indexWriterConfig);
		_searcherManager = new SearcherManager(_writer, applyAllDeletes: true, null);
		_recoverySearcher = new IndexSearcher(DirectoryReader.Open(_writer, applyAllDeletes: true));
		_logger.LogDebug("Opened Lucene index {Index}", index);
	}

	public void Add(ulong id, TFPos position, string keys, string values, bool forceUpdate = false) {
		var idStr = id.ToString();
		var term = new Term(IdField, idStr);

		if (forceUpdate) {
			_writer.UpdateDocument(term, BuildDocument(idStr, keys, values));
			_dirty = true;
			return;
		}

		if (_recoverySearcher != null) {
			var hits = _recoverySearcher.Search(new TermQuery(term), 1);
			if (hits.TotalHits > 0)
				return;
			_recoverySearcher.IndexReader.Dispose();
			_recoverySearcher = null;
			_logger.LogDebug("Lucene index {Index} exited recovery probe", _name);
		}

		_writer.AddDocument(BuildDocument(idStr, keys, values));
		_dirty = true;
	}

	static Document BuildDocument(string id, string keys, string values) {
		var doc = new Document { new StringField(IdField, id, Field.Store.YES) };
		if (keys.Length > 0)
			doc.Add(new TextField("keys", keys, Field.Store.NO) { Boost = 1.0f });
		if (values.Length > 0)
			doc.Add(new TextField("values", values, Field.Store.NO) { Boost = 5.0f });
		return doc;
	}

	public void Refresh() {
		if (!_dirty)
			return;
		_searcherManager.MaybeRefresh();
		_dirty = false;
	}

	public TFPos Flush(TFPos current, bool force) {
		_writer.Commit();
		return current;
	}

	public IEnumerable<ulong> Search(IReadOnlyList<string> keywords, IReadOnlyList<string> excludeWords, int limit) {
		if (keywords.Count == 0)
			return [];

		var boolQuery = new BooleanQuery();
		foreach (var rawTerm in keywords) {
			var t = rawTerm.ToLowerInvariant();
			boolQuery.Add(new TermQuery(new Term("keys", t)), Occur.SHOULD);
			boolQuery.Add(new TermQuery(new Term("values", t)), Occur.SHOULD);
		}
		AddExclusions(boolQuery, excludeWords);

		return RunQuery(boolQuery, limit);
	}

	public IEnumerable<ulong> List(int limit) => RunQuery(new MatchAllDocsQuery(), limit);

	static void AddExclusions(BooleanQuery query, IReadOnlyList<string> excludeWords) {
		foreach (var rawTerm in excludeWords) {
			var t = rawTerm.ToLowerInvariant();
			query.Add(new TermQuery(new Term("keys", t)), Occur.MUST_NOT);
			query.Add(new TermQuery(new Term("values", t)), Occur.MUST_NOT);
		}
	}

	List<ulong> RunQuery(Query query, int limit) {
		var searcher = _searcherManager.Acquire();
		try {
			var topDocs = searcher.Search(query, limit);
			return ReadHits(searcher, topDocs);
		} finally {
			_searcherManager.Release(searcher);
		}
	}

	static List<ulong> ReadHits(IndexSearcher searcher, TopDocs topDocs) {
		var results = new List<ulong>(topDocs.ScoreDocs.Length);
		foreach (var scoreDoc in topDocs.ScoreDocs) {
			var doc = searcher.Doc(scoreDoc.Doc, SingleField);
			results.Add(ulong.Parse(doc.Get(IdField)!));
		}
		return results;
	}

	static readonly HashSet<string> SingleField = [IdField];

	public void Dispose() {
		_recoverySearcher?.IndexReader.Dispose();
		_searcherManager.Dispose();
		_writer.Dispose();
		_analyzer.Dispose();
	}
}
