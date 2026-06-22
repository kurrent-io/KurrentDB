// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Embeddings;

/// <summary>
/// Cross-workspace embedding cache.
/// </summary>
public sealed class EmbeddingCache(
	WorkspaceRegistry workspaces,
	StoreRegistry<IVectorStore> vectorStores,
	IEmbeddingGenerator<string, Embedding<float>> generator,
	ILoggerFactory loggerFactory) {

	const int MaxWorkspaceProbes = 5;

	readonly ConcurrentDictionary<string, WorkspaceBloomFilters> _filters = new(StringComparer.Ordinal);
	readonly ConcurrentDictionary<CacheKey, TaskCompletionSource<ReadOnlyMemory<float>>> _inFlight = new();
	readonly ILogger<EmbeddingCache> _logger = loggerFactory.CreateLogger<EmbeddingCache>();

	enum DocKind { LogPosition, StreamHash }

	readonly record struct CacheKey(DocKind Kind, ulong DocId);

	readonly record struct PendingGeneration(
		int Index, TaskCompletionSource<ReadOnlyMemory<float>> Tcs, CacheKey Key);

	struct BatchPlan {
		public List<PendingGeneration>? ToGenerate;
		public List<string>? GeneratorTexts;

		public List<(int Index, Task<ReadOnlyMemory<float>> Task)>? Awaiting;
		public int Hits;
	}

	public void MountBloomFilters(string workspace, WorkspaceBloomFilters filters) =>
		_filters[workspace] = filters;

	public void UnmountBloomFilters(string workspace) =>
		_filters.TryRemove(workspace, out _);

	public ValueTask GetOrCreateEventEmbeddingsAsync(
		string requester,
		ReadOnlyMemory<ulong> preparePositions,
		ReadOnlyMemory<string> streamNames,
		ReadOnlyMemory<IndexKind> kinds,
		ReadOnlyMemory<string> texts,
		Memory<ReadOnlyMemory<float>> results,
		CancellationToken ct) =>
		GetOrCreateAsync(requester, preparePositions, streamNames, kinds, texts, results, ct);

	public ValueTask GetOrCreateStreamEmbeddingsAsync(
		string requester,
		ReadOnlyMemory<ulong> streamHashes,
		ReadOnlyMemory<string> streamNames,
		ReadOnlyMemory<IndexKind> kinds,
		ReadOnlyMemory<string> texts,
		Memory<ReadOnlyMemory<float>> results,
		CancellationToken ct) =>
		GetOrCreateAsync(requester, streamHashes, streamNames, kinds, texts, results, ct);

	async ValueTask GetOrCreateAsync(
		string requester,
		ReadOnlyMemory<ulong> docIds,
		ReadOnlyMemory<string> streamNames,
		ReadOnlyMemory<IndexKind> kinds,
		ReadOnlyMemory<string> texts,
		Memory<ReadOnlyMemory<float>> results,
		CancellationToken ct) {
		var count = docIds.Length;
		if (count == 0)
			return;

		var plan = ResolveBatch(requester, docIds.Span, streamNames.Span, kinds.Span, texts.Span, results.Span);

		if (plan.ToGenerate is not null)
			await GenerateMissesAsync(plan.ToGenerate, plan.GeneratorTexts!, results, ct).ConfigureAwait(false);

		if (plan.Awaiting is not null)
			await AwaitInFlightAsync(plan.Awaiting, results, ct).ConfigureAwait(false);

		if (plan.Hits > 0)
			_logger.LogTrace("Embedding cache: {Hits}/{Total} hits for workspace '{Workspace}'",
				plan.Hits, count, requester);
	}

	BatchPlan ResolveBatch(string requester, ReadOnlySpan<ulong> docIds, ReadOnlySpan<string> streamNames,
		ReadOnlySpan<IndexKind> kinds, ReadOnlySpan<string> texts, Span<ReadOnlyMemory<float>> results) {
		var plan = default(BatchPlan);
		for (var i = 0; i < docIds.Length; i++) {
			var docId = docIds[i];
			var kind = kinds[i];

			if (TryFindVector(workspaces.All, requester, streamNames[i], kind, docId, out var found)) {
				results[i] = found;
				plan.Hits++;
				continue;
			}

			var key = new CacheKey(KeyspaceOf(kind), docId);

			if (_inFlight.TryGetValue(key, out var existing)) {
				(plan.Awaiting ??= []).Add((i, existing.Task));
				continue;
			}

			var tcs = new TaskCompletionSource<ReadOnlyMemory<float>>(TaskCreationOptions.RunContinuationsAsynchronously);
			var winner = _inFlight.GetOrAdd(key, tcs);
			if (winner != tcs) {
				(plan.Awaiting ??= []).Add((i, winner.Task));
			} else {
				(plan.ToGenerate ??= []).Add(new(i, tcs, key));
				(plan.GeneratorTexts ??= []).Add(texts[i]);
			}
		}
		return plan;
	}

	async Task GenerateMissesAsync(IReadOnlyList<PendingGeneration> pending, IReadOnlyList<string> texts, Memory<ReadOnlyMemory<float>> results, CancellationToken ct) {
		try {
			var generated = await generator.GenerateAsync(texts, EmbeddingPurpose.DocumentOptions, ct).ConfigureAwait(false);
			var span = results.Span;
			for (var j = 0; j < pending.Count; j++) {
				var vec = generated[j].Vector;
				var p = pending[j];
				span[p.Index] = vec;
				p.Tcs.SetResult(vec);
				_inFlight.TryRemove(p.Key, out _);
			}
		} catch (Exception ex) {
			foreach (var p in pending) {
				p.Tcs.TrySetException(ex);
				_inFlight.TryRemove(p.Key, out _);
			}
			throw;
		}
	}

	static async Task AwaitInFlightAsync(IReadOnlyList<(int Index, Task<ReadOnlyMemory<float>> Task)> awaiting,
		Memory<ReadOnlyMemory<float>> results, CancellationToken ct) {
		foreach (var (index, task) in awaiting) {
			var vec = await task.WaitAsync(ct).ConfigureAwait(false);
			results.Span[index] = vec;
		}
	}

	bool TryFindVector(IReadOnlyList<WorkspaceEntry> allWorkspaces, string requester, string streamName,
		IndexKind kind, ulong docId, out ReadOnlyMemory<float> vector) {
		if (kind is IndexKind.Memory or IndexKind.MemoryStreams) {
			vector = default!;
			return false;
		}

		if (kind == IndexKind.EventsStreams && TryReadFrom(requester, kind, docId, out vector))
			return true;

		var probed = 0;
		for (var w = 0; w < allWorkspaces.Count; w++) {
			var workspace = allWorkspaces[w];
			if (string.Equals(workspace.Name, requester, StringComparison.Ordinal))
				continue;
			if (!Matches(workspace, streamName))
				continue;
			if (TryReadFrom(workspace.Name, kind, docId, out vector))
				return true;
			if (++probed >= MaxWorkspaceProbes)
				break;
		}

		vector = default!;
		return false;
	}

	static bool Matches(WorkspaceEntry workspace, string streamName) {
		if (WorkspaceNaming.IsDefault(workspace.Name))
			return true;

		foreach (var rule in workspace.FilterRules)
			if (streamName.StartsWith(rule.StreamPrefix, StringComparison.Ordinal))
				return true;

		return false;
	}

	bool TryReadFrom(string workspace, IndexKind kind, ulong docId, out ReadOnlyMemory<float> vector) {
		if (_filters.TryGetValue(workspace, out var bloom)
			&& (KeyspaceOf(kind) == DocKind.StreamHash ? bloom.MightContainStreamHash(docId) : bloom.MightContainLogPosition(docId))
			&& vectorStores.TryGet(new WorkspaceIndex(workspace, kind), out var store)
			&& TryReadVector(store!, docId, out vector))
			return true;

		vector = default!;
		return false;
	}

	static DocKind KeyspaceOf(IndexKind kind) =>
		kind is IndexKind.EventsStreams or IndexKind.MemoryStreams ? DocKind.StreamHash : DocKind.LogPosition;

	static bool TryReadVector(IVectorStore store, ulong id, out ReadOnlyMemory<float> vector) {
		try {
			return store.TryGet(id, out vector);
		} catch (ObjectDisposedException) {
			vector = default;
			return false;
		}
	}
}
