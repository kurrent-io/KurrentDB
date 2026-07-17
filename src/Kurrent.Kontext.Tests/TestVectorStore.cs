// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Minimal in-memory <see cref="VectorStore"/> double. No public connector implements
/// Microsoft.Extensions.VectorData.Abstractions 10.x yet (the SK InMemory connector is built against
/// the 9.x-era API and fails with <c>TypeLoadException</c>), so the tests carry their own: dictionary
/// rows, compiled LINQ filters, honored OrderBy, cosine ranking over the supplied generator. Typed
/// directly against <see cref="MemoryRecord"/> via InternalsVisibleTo — the core only ever asks for
/// one collection shape.
/// </summary>
sealed class TestVectorStore(IEmbeddingGenerator<string, Embedding<float>> generator) : VectorStore {
	readonly Dictionary<string, object> _collections = [];

	[RequiresDynamicCode("Matches the base contract; the test double does not generate code."), RequiresUnreferencedCode("Matches the base contract; the test double does not reflect.")]
	public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null) {
		if (!_collections.TryGetValue(name, out var collection))
			_collections[name] = collection = new TestMemoryCollection(name, generator);
		return (VectorStoreCollection<TKey, TRecord>)collection;
	}

	[RequiresDynamicCode("Matches the base contract."), RequiresUnreferencedCode("Matches the base contract.")]
	public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition) =>
		throw new NotSupportedException();

	public override IAsyncEnumerable<string> ListCollectionNamesAsync(CancellationToken cancellationToken = default) =>
		_collections.Keys.ToAsyncEnumerable();

	public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default) =>
		Task.FromResult(_collections.ContainsKey(name));

	public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default) {
		_collections.Remove(name);
		return Task.CompletedTask;
	}

	public override object? GetService(Type serviceType, object? serviceKey = null) => null;
}

sealed class TestMemoryCollection(string name, IEmbeddingGenerator<string, Embedding<float>> generator) : VectorStoreCollection<string, MemoryRecord> {
	// The touch buffer flushes from thread-pool threads, so row access is locked.
	readonly Lock _lock = new();
	readonly Dictionary<string, (MemoryRecord Record, float[] Vector)> _rows = [];

	// Batching observability for the tests: how many upsert calls landed, covering how many records.
	public int UpsertBatchCalls { get; private set; }
	public int UpsertedRecords  { get; private set; }

	public override string Name => name;

	public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

	public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default) {
		_rows.Clear();
		return Task.CompletedTask;
	}

	public override Task<MemoryRecord?> GetAsync(string key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default) {
		lock (_lock)
			return Task.FromResult(_rows.TryGetValue(key, out var row) ? row.Record : null);
	}

	public override async IAsyncEnumerable<MemoryRecord> GetAsync(
		Expression<Func<MemoryRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<MemoryRecord>? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default) {
		await Task.Yield();

		List<MemoryRecord> snapshot;
		lock (_lock)
			snapshot = _rows.Values.Select(r => r.Record).ToList();

		var predicate = filter.Compile();
		var matches   = snapshot.Where(predicate);

		if (options?.OrderBy is { } orderBy) {
			IOrderedEnumerable<MemoryRecord>? ordered = null;
			foreach (var sort in orderBy(new FilteredRecordRetrievalOptions<MemoryRecord>.OrderByDefinition()).Values) {
				var key = sort.PropertySelector.Compile();
				ordered = ordered is null
					? sort.Ascending ? matches.OrderBy(key) : matches.OrderByDescending(key)
					: sort.Ascending ? ordered.ThenBy(key) : ordered.ThenByDescending(key);
			}

			if (ordered is not null)
				matches = ordered;
		}

		foreach (var record in matches.Take(top))
			yield return record;
	}

	public override async Task UpsertAsync(MemoryRecord record, CancellationToken cancellationToken = default) {
		var vector = (await generator.GenerateAsync([record.Embedding], cancellationToken: cancellationToken))[0].Vector.ToArray();

		lock (_lock) {
			_rows[record.MemoryId] = (record, vector);
			UpsertBatchCalls++;
			UpsertedRecords++;
		}
	}

	public override async Task UpsertAsync(IEnumerable<MemoryRecord> records, CancellationToken cancellationToken = default) {
		var batch = records.ToList();
		if (batch.Count == 0)
			return;

		// One generator call for the whole batch — the same amortization a real connector gets.
		var embeddings = await generator.GenerateAsync(batch.Select(r => r.Embedding), cancellationToken: cancellationToken);

		lock (_lock) {
			for (var i = 0; i < batch.Count; i++)
				_rows[batch[i].MemoryId] = (batch[i], embeddings[i].Vector.ToArray());

			UpsertBatchCalls++;
			UpsertedRecords += batch.Count;
		}
	}

	public override Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
		lock (_lock)
			_rows.Remove(key);
		return Task.CompletedTask;
	}

	public override async IAsyncEnumerable<VectorSearchResult<MemoryRecord>> SearchAsync<TInput>(
		TInput searchValue, int top, VectorSearchOptions<MemoryRecord>? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default) {
		var query     = (await generator.GenerateAsync([(string)(object)searchValue], cancellationToken: cancellationToken))[0].Vector.ToArray();
		var predicate = options?.Filter?.Compile() ?? (_ => true);

		List<(MemoryRecord Record, float[] Vector)> snapshot;
		lock (_lock)
			snapshot = _rows.Values.ToList();

		var hits = snapshot
			.Where(r => predicate(r.Record))
			.Select(r => new VectorSearchResult<MemoryRecord>(r.Record, Cosine(query, r.Vector)))
			.OrderByDescending(h => h.Score)
			.Take(top);

		foreach (var hit in hits)
			yield return hit;
	}

	public override object? GetService(Type serviceType, object? serviceKey = null) => null;

	static double Cosine(float[] a, float[] b) {
		double dot = 0, na = 0, nb = 0;
		for (var i = 0; i < a.Length; i++) {
			dot += a[i] * b[i];
			na  += a[i] * a[i];
			nb  += b[i] * b[i];
		}

		return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
	}
}

/// <summary>
/// Deterministic 384-dim embedding from character trigrams, L2-normalized — same text embeds
/// identically within one process, overlapping text embeds nearby. Enough to exercise ranking
/// without a real model.
/// </summary>
sealed class TrigramHashEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
	public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
		Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v)))));

	public object? GetService(Type serviceType, object? serviceKey = null) => null;

	public void Dispose() { }

	static float[] Embed(string text) {
		var vector = new float[MemoryRecord.EmbeddingDimensions];
		var lower  = text.ToLowerInvariant();

		for (var i = 0; i + 2 < lower.Length; i++) {
			var hash = HashCode.Combine(lower[i], lower[i + 1], lower[i + 2]);
			vector[(int)((uint)hash % MemoryRecord.EmbeddingDimensions)] += 1;
		}

		var norm = MathF.Sqrt(vector.Sum(x => x * x));
		if (norm > 0)
			for (var i = 0; i < vector.Length; i++)
				vector[i] /= norm;

		return vector;
	}
}
