// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Events;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Kontext.Embeddings;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.LogCommon;
using Microsoft.Extensions.AI;

namespace KurrentDB.Kontext.Search;

public readonly record struct SearchHit(
	string Stream,
	long? EventNumber,
	long PreparePosition,
	long? CommitPosition,
	string EventType,
	DateTime Timestamp,
	ReadOnlyMemory<byte> Data,
	ReadOnlyMemory<byte> Metadata,
	float Score);

public readonly record struct StreamHit(string Name, float Score);

public interface IKontextService {
	IAsyncEnumerable<SearchHit> SearchAsync(string workspace, string keywords, string? query = null,
		string? streamFilter = null, string? eventTypeFilter = null, CancellationToken ct = default);
	IAsyncEnumerable<SearchHit> RecallAsync(string workspace, string keywords, string? query = null,
		CancellationToken ct = default);
	IAsyncEnumerable<StreamHit> DiscoverStreams(string workspace, string? keywords, int limit, IndexKind kind = IndexKind.EventsStreams, CancellationToken ct = default);
}

public class KontextService : IKontextService {
	readonly IIndexBackend<string> _indexBackend;
	readonly IReadIndex<string> _readIndex;
	readonly Retriever _retriever;
	readonly WorkspaceRegistry _workspaces;
	readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
	readonly IReranker _reranker;
	readonly NounPhraseExtractor _npe;
	readonly KontextReadySignal _ready;
	volatile bool _isReady;

	public KontextService(
		IIndexBackend<string> indexBackend,
		IReadIndex<string> readIndex,
		Retriever retriever,
		WorkspaceRegistry workspaces,
		IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
		IReranker reranker,
		NounPhraseExtractor npe,
		KontextReadySignal ready) {
		_indexBackend = indexBackend;
		_readIndex = readIndex;
		_retriever = retriever;
		_workspaces = workspaces;
		_embeddingGenerator = embeddingGenerator;
		_reranker = reranker;
		_npe = npe;
		_ready = ready;
	}

	public IAsyncEnumerable<SearchHit> SearchAsync(string workspace, string keywords, string? query = null,
		string? streamFilter = null, string? eventTypeFilter = null, CancellationToken ct = default) =>
			RunSearchAsync(
			workspace: workspace, kind: IndexKind.Events,
			keywords, query: query, includeStreams: false,
			streamFilter: streamFilter, eventTypeFilter: eventTypeFilter, ct: ct);

	public IAsyncEnumerable<SearchHit> RecallAsync(string workspace, string keywords, string? query = null,
		CancellationToken ct = default) =>
		RunSearchAsync(
			workspace: workspace, kind: IndexKind.Memory,
			keywords, query: query, includeStreams: true, ct: ct);

	async IAsyncEnumerable<SearchHit> RunSearchAsync(string workspace, IndexKind kind, string keywords, string? query,
		bool includeStreams, string? streamFilter = null, string? eventTypeFilter = null,
		[EnumeratorCancellation] CancellationToken ct = default) {

		if (kind is not (IndexKind.Events or IndexKind.Memory))
			throw new ArgumentException($"{nameof(RunSearchAsync)} supports only the event indexes; got {kind}.", nameof(kind));

		if (!_workspaces.TryGetRunning(workspace, out _))
			throw new WorkspaceNotRunningException(workspace);

		if (!_isReady) {
			await _ready.WaitAsync();
			_isReady = true;
		}

		var index = new WorkspaceIndex(workspace, kind);

		var tokens = keywords.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var include = new List<string>(tokens.Length);
		var exclude = new List<string>();
		foreach (var t in tokens) {
			if (t.StartsWith('~')) {
				if (t.Length > 1)
					exclude.Add(t[1..]);
			} else {
				include.Add(t);
			}
		}

		var searchTerms = string.Join(" ", include);
		if (string.IsNullOrWhiteSpace(searchTerms))
			yield break;

		var extracted = _npe.ExtractKeywords(searchTerms);
		var bm25Terms = extracted.Count > 0 ? extracted : include;
		var embeddings = await _embeddingGenerator.GenerateAsync([searchTerms], EmbeddingPurpose.QueryOptions, ct);
		var queryEmbedding = embeddings[0].Vector;

		// Over-fetch when filters are present since they're applied post-resolution and
		// can drop a large fraction of the initial candidates.
		var hasFilters = streamFilter != null || eventTypeFilter != null || exclude.Count > 0;
		var fetchCount = hasFilters ? SearchAlgorithms.RetrievalCandidates * 3 : SearchAlgorithms.RetrievalCandidates;

		// 1. Retrieve candidates
		var candidates = _retriever.HybridSearch(
			index, bm25Terms, exclude, queryEmbedding, fetchCount);

		// 1b. Optionally also search the stream-name index and fold each matched stream's latest
		//     event into the candidate pool.
		if (includeStreams) {
			var streamMatches = await FindCandidatesInStreamNameIndex(
				workspace, kind, include, exclude, queryEmbedding, SearchAlgorithms.RetrievalCandidates, ct);
			if (streamMatches.Count > 0) {
				var fused = new Dictionary<ulong, float>(candidates.Count + streamMatches.Count);
				SearchAlgorithms.AccumulateRrfScores(candidates, fused, candidates.Count);
				SearchAlgorithms.AccumulateRrfScores(streamMatches, fused, streamMatches.Count);
				candidates = SearchAlgorithms.NormalizeRrfScores(fused);
			}
		}

		if (candidates.Count == 0)
			yield break;

		// 2. Hydrate full events from KurrentDB, applying filters per-candidate as they're hydrated.
		//    Returns only the candidates whose hydration survived filtering, preserving order.
		var docs = new Dictionary<ulong, HydratedDoc>(candidates.Count);
		candidates = await HydrateCandidates(
			candidates, docs, streamFilter, eventTypeFilter, exclude.Count > 0 ? exclude : null, ct);

		if (candidates.Count == 0)
			yield break;

		// 3. Pool refinement via a second-stage RRF over (stage-1 RRF rank, CE rank).
		//    Pool = top-N by RRF ∪ top-N by CE. Each pool member's relevance becomes
		//    1/(k+rrfRank) + 1/(k+ceRank), with absent terms contributing 0.
		//    Items appearing in both rankings are naturally rewarded.
		if (!string.IsNullOrEmpty(query)) {
			var buffer = new StringBuilder();
			var rerankerInputs = new List<(ulong Id, string Document)>(candidates.Count);

			foreach (var c in candidates) {
				var doc = docs[c.DocId];
				rerankerInputs.Add((c.DocId,
					EventTextHelpers.BuildEventText(doc.EventType, doc.Data.Span, buffer)));
			}

			if (rerankerInputs.Count > 0) {
				var poolSize = SearchAlgorithms.MaxResults;
				var rerankerTopIds = new List<ulong>(poolSize);
				await foreach (var r in _reranker.RerankAsync(query, rerankerInputs, ct)) {
					if (rerankerTopIds.Count >= poolSize)
						break;
					rerankerTopIds.Add(r.DocId);
				}

				var rerankedScores = new Dictionary<ulong, float>(poolSize * 2);
				SearchAlgorithms.AccumulateRrfScores(candidates, rerankedScores, poolSize);
				SearchAlgorithms.AccumulateRrfScores(rerankerTopIds, rerankedScores);

				candidates = SearchAlgorithms.NormalizeRrfScores(rerankedScores);
			}
		}

		// 4. Attach embeddings if a vector store is available; otherwise fall back to
		//    token-set Jaccard on the hydrated document so MMR still has a diversity signal.
		if (!_retriever.TryAttachEmbeddings(index, docs)) {
			var buffer = new StringBuilder();
			foreach (var c in candidates) {
				var doc = docs[c.DocId];
				var text = EventTextHelpers.BuildEventText(doc.EventType, doc.Data.Span, buffer);
				doc.Tokens = [.. _npe.ExtractKeywords(text)];
			}
		}

		// 5. MMR picks the final K diverse results from the refined pool.
		var mmrSelected = SearchAlgorithms.MmrSelect(candidates, docs, SearchAlgorithms.MaxResults);

		foreach (var (docId, score) in mmrSelected) {
			var doc = docs[docId];
			yield return new SearchHit(
				Stream: doc.Stream,
				EventNumber: doc.EventNumber,
				PreparePosition: (long)docId,
				CommitPosition: doc.HasCommitRecord ? null : (long)docId,
				EventType: doc.EventType,
				Timestamp: doc.Timestamp,
				Data: doc.IsJson ? doc.Data : default,
				Metadata: doc.Metadata,
				Score: MathF.Round(score, 4));
		}
	}

	/// <summary>
	/// Each candidate's DocId is the event's prepare position. We read the prepare
	/// record directly from the transaction file
	/// </summary>
	async Task<IReadOnlyList<ScoredDoc>> HydrateCandidates(
		IReadOnlyList<ScoredDoc> candidates,
		Dictionary<ulong, HydratedDoc> docs,
		string? streamFilter,
		string? eventTypeFilter,
		IReadOnlyList<string>? excludeWords,
		CancellationToken ct = default) {
		var matchStream = streamFilter != null ? CompilePattern(streamFilter) : null;
		var matchType = eventTypeFilter != null ? CompilePattern(eventTypeFilter) : null;
		var lowerExcludes = excludeWords?.Select(w => w.ToLowerInvariant()).ToArray();
		var survivors = new List<ScoredDoc>(candidates.Count);
		foreach (var (preparePos, score) in candidates) {
			var result = await _indexBackend.TFReader.TryReadAt((long)preparePos, couldBeScavenged: true, ct);
			if (!result.Success)
				continue;
			if (result.LogRecord is not IPrepareLogRecord<string> prepare)
				continue;
			if (prepare.RecordType is not (LogRecordType.Prepare or LogRecordType.Stream or LogRecordType.EventType))
				continue;
			if (matchStream != null && !matchStream(prepare.EventStreamId))
				continue;
			if (matchType != null && !matchType(prepare.EventType))
				continue;
			if (lowerExcludes != null) {
				var document = Encoding.UTF8.GetString(prepare.Data.Span).ToLowerInvariant();
				if (lowerExcludes.Any(document.Contains))
					continue;
			}

			// Only committed prepares carry an EventNumber (ExpectedVersion + 1). Transactional
			// prepares need a commit-record lookup to resolve theirs — leave it null.
			docs[preparePos] = new HydratedDoc {
				Stream = prepare.EventStreamId,
				EventNumber = prepare.Flags.HasFlag(PrepareFlags.IsCommitted)
					? prepare.ExpectedVersion + 1
					: null,
				EventType = prepare.EventType,
				Timestamp = prepare.TimeStamp,
				Data = prepare.Data,
				IsJson = prepare.Flags.HasFlag(PrepareFlags.IsJson),
				Metadata = prepare.Metadata,
			};
			survivors.Add(new(preparePos, score));
		}
		return survivors;
	}

	static Func<string, bool> CompilePattern(string pattern) {
		if (!pattern.Contains('*'))
			return s => s == pattern;
		var regex = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$",
			RegexOptions.Compiled);
		return regex.IsMatch;
	}

	public async IAsyncEnumerable<StreamHit> DiscoverStreams(
		string workspace, string? keywords, int limit,
		IndexKind kind = IndexKind.EventsStreams,
		[EnumeratorCancellation] CancellationToken ct = default) {
		if (kind != IndexKind.EventsStreams && kind != IndexKind.MemoryStreams)
			throw new ArgumentException($"{nameof(DiscoverStreams)} supports only the streams indexes; got {kind}.", nameof(kind));
		if (!_workspaces.TryGetRunning(workspace, out _))
			throw new WorkspaceNotRunningException(workspace);

		var tokens = (keywords ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var include = new List<string>(tokens.Length);
		var exclude = new List<string>();
		foreach (var t in tokens) {
			if (t.StartsWith('~')) {
				if (t.Length > 1)
					exclude.Add(t[1..]);
			} else {
				include.Add(t);
			}
		}

		var index = new WorkspaceIndex(workspace, kind);

		IReadOnlyList<ScoredDoc> candidates;
		if (include.Count == 0) {
			candidates = _retriever.ListDocs(index, limit);
		} else {
			var searchTerms = string.Join(' ', include);
			var queryEmbeddings = await _embeddingGenerator.GenerateAsync([searchTerms], EmbeddingPurpose.QueryOptions, ct);
			candidates = _retriever.HybridSearch(index, include, exclude, queryEmbeddings[0].Vector, limit);
		}

		var yielded = 0;
		foreach (var candidate in candidates) {
			if (yielded == limit)
				yield break;
			var name = await TryResolveStreamNameAsync(candidate.DocId, ct);
			if (name == null)
				continue;
			yield return new StreamHit(name, candidate.Score);
			yielded++;
		}
	}

	/// <summary>
	/// Hybrid-searches the stream-name index corresponding to <paramref name="eventKind"/> and
	/// resolves each matched stream to its latest event's prepare position.
	/// </summary>
	async ValueTask<IReadOnlyList<ScoredDoc>> FindCandidatesInStreamNameIndex(
		string workspace, IndexKind eventKind, IReadOnlyList<string> include, IReadOnlyList<string> exclude,
		ReadOnlyMemory<float> queryEmbedding, int limit, CancellationToken ct) {
		if (include.Count == 0)
			return [];

		var streamsIndex = new WorkspaceIndex(workspace, StreamsIndexFor(eventKind));
		var streamHits = _retriever.HybridSearch(streamsIndex, include, exclude, queryEmbedding, limit);
		if (streamHits.Count == 0)
			return [];

		var matches = new List<ScoredDoc>(streamHits.Count);
		foreach (var hit in streamHits) {
			var name = await TryResolveStreamNameAsync(hit.DocId, ct);
			if (name == null)
				continue;

			var read = await _readIndex.ReadEvent(name, _readIndex.GetStreamId(name), -1, ct);
			if (read.Result != ReadEventResult.Success)
				continue;

			matches.Add(hit with { DocId = (ulong)read.Record.LogPosition });
		}

		return matches;
	}

	static IndexKind StreamsIndexFor(IndexKind eventKind) => eventKind switch {
		IndexKind.Events => IndexKind.EventsStreams,
		IndexKind.Memory => IndexKind.MemoryStreams,
		_ => throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, "Not an event index."),
	};

	async ValueTask<string?> TryResolveStreamNameAsync(ulong hash, CancellationToken ct) {
		var info = await _readIndex.ReadEventInfoForward_NoCollisions(
			hash, fromEventNumber: 0, maxCount: 1, beforePosition: long.MaxValue, ct);
		if (info.EventInfos.Length == 0 && info.NextEventNumber >= 0) {
			info = await _readIndex.ReadEventInfoForward_NoCollisions(
				hash, info.NextEventNumber, maxCount: 1, beforePosition: long.MaxValue, ct);
		}
		if (info.EventInfos.Length == 0)
			return null;

		var result = await _indexBackend.TFReader.TryReadAt(
			info.EventInfos[0].LogPosition, couldBeScavenged: true, ct);
		if (!result.Success || result.LogRecord is not IPrepareLogRecord<string> prepare)
			return null;
		return prepare.EventStreamId;
	}
}
