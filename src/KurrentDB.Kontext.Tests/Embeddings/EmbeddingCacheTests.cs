// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class EmbeddingCacheTests {
	// Collection expressions can't target ReadOnlyMemory<T>; these keep call sites terse.
	static ReadOnlyMemory<ulong> Ids(params ulong[] ids) => ids;
	static ReadOnlyMemory<string> Txt(params string[] texts) => texts;
	static ReadOnlyMemory<string> Streams(params string[] streams) => streams;
	static ReadOnlyMemory<IndexKind> Kinds(params IndexKind[] kinds) => kinds;

	sealed class CountingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
		public int CallCount;
		public int TextCount;

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
			CancellationToken cancellationToken = default) {
			Interlocked.Increment(ref CallCount);
			var result = new GeneratedEmbeddings<Embedding<float>>();
			foreach (var v in values) {
				Interlocked.Increment(ref TextCount);
				result.Add(new Embedding<float>(VectorFor(v)));
			}
			return Task.FromResult(result);
		}

		public static float[] VectorFor(string text) {
			// Deterministic 4-dim vector derived from text length and first char.
			var v = new float[4];
			v[0] = text.Length;
			v[1] = text.Length > 0 ? text[0] : 0;
			v[2] = 1f;
			v[3] = 0.5f;
			return v;
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }
	}

	sealed class InMemoryVectorStore : IVectorStore {
		readonly Dictionary<ulong, float[]> _store = new();
		public bool DisposedThrowOnAccess;
		public int Probes;

		public void Add(ulong id, TFPos position, ReadOnlyMemory<float> vector) => _store[id] = vector.ToArray();

		public bool TryGet(ulong id, out ReadOnlyMemory<float> vector) {
			Probes++;
			if (DisposedThrowOnAccess)
				throw new ObjectDisposedException(nameof(InMemoryVectorStore));
			if (_store.TryGetValue(id, out var arr)) { vector = arr; return true; }
			vector = default; return false;
		}

		public IEnumerable<ulong> Search(ReadOnlyMemory<float> query, int limit) => _store.Keys.Take(limit);
		public TFPos Flush(TFPos current, bool force) => current;
		public void Dispose() { }
	}

	sealed class Harness : IDisposable {
		public CountingGenerator Generator { get; } = new();
		public WorkspaceRegistry Workspaces { get; } = new();
		public StoreRegistry<IVectorStore> VectorStores { get; } = new();
		public EmbeddingCache Cache { get; }
		readonly List<string> _tempDirs = [];
		readonly Dictionary<string, WorkspaceBloomFilters> _filters = new();

		public Harness() {
			Cache = new EmbeddingCache(Workspaces, VectorStores, Generator, NullLoggerFactory.Instance);
		}

		public WorkspaceEntry AddWorkspace(string name, params FilterRule[] rules) {
			var entry = WorkspaceEntry.Create(name, rules, true, true, false, false, false, false);
			Workspaces.Upsert(entry);
			VectorStores.TryAdd(new WorkspaceIndex(name, IndexKind.Events), new InMemoryVectorStore());
			VectorStores.TryAdd(new WorkspaceIndex(name, IndexKind.Memory), new InMemoryVectorStore());
			VectorStores.TryAdd(new WorkspaceIndex(name, IndexKind.EventsStreams), new InMemoryVectorStore());
			VectorStores.TryAdd(new WorkspaceIndex(name, IndexKind.MemoryStreams), new InMemoryVectorStore());

			var dir = Path.Combine(Path.GetTempPath(), $"kontext-cachetest-{Guid.NewGuid():N}");
			_tempDirs.Add(dir);
			var f = new WorkspaceBloomFilters(dir, NullLogger<WorkspaceBloomFilters>.Instance);
			_filters[name] = f;
			Cache.MountBloomFilters(name, f);
			return entry;
		}

		public InMemoryVectorStore Store(string workspace, IndexKind kind) {
			VectorStores.TryGet(new WorkspaceIndex(workspace, kind), out var s);
			return (InMemoryVectorStore)s!;
		}

		public WorkspaceBloomFilters Filters(string workspace) => _filters[workspace];

		public void Dispose() {
			foreach (var f in _filters.Values) f.Dispose();
			foreach (var d in _tempDirs)
				if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
		}
	}

	// --- Baseline: nothing to reuse ---

	[Test]
	public async Task Generates_When_No_Cache_Hit_Anywhere() {
		using var h = new Harness();
		h.AddWorkspace("solo");

		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"solo", Ids(100), Streams("orders-1"), Kinds(IndexKind.Events), Txt("hello"), CancellationToken.None);

		await Assert.That(result.Length).IsEqualTo(1);
		await Assert.That(result[0].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("hello"));
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
		await Assert.That(h.Generator.TextCount).IsEqualTo(1);
	}

	// --- Reuse across workspaces ---

	[Test]
	public async Task Reuses_Event_Vector_From_Other_Workspace() {
		using var h = new Harness();
		h.AddWorkspace(WorkspaceNaming.DefaultName);
		h.AddWorkspace("alpha", new FilterRule("orders-", null));

		// Default has already embedded position 100.
		var seeded = CountingGenerator.VectorFor("payload");
		h.Store(WorkspaceNaming.DefaultName, IndexKind.Events).Add(100, seeded);
		h.Filters(WorkspaceNaming.DefaultName).AddLogPosition(100);

		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"alpha", Ids(100), Streams("orders-1"), Kinds(IndexKind.Events), Txt("payload"), CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(seeded);
		await Assert.That(h.Generator.CallCount).IsEqualTo(0);
	}

	[Test]
	public async Task Reuses_Stream_Vector_From_Other_Workspace() {
		using var h = new Harness();
		h.AddWorkspace(WorkspaceNaming.DefaultName);
		h.AddWorkspace("alpha");

		// Default already embedded the name of stream "orders-1" (EventsStreams).
		var vec = new float[] { 5, 5, 5, 5 };
		h.Store(WorkspaceNaming.DefaultName, IndexKind.EventsStreams).Add(7777, vec);
		h.Filters(WorkspaceNaming.DefaultName).AddStreamHash(7777);

		var result = await h.Cache.GetOrCreateStreamEmbeddingsAsync(
			"alpha", Ids(7777), Streams("orders-1"), Kinds(IndexKind.EventsStreams), Txt("orders 1"), CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(vec);
		await Assert.That(h.Generator.CallCount).IsEqualTo(0);
	}

	[Test]
	public async Task Reuses_Vector_From_Prefix_Matching_Sibling_Without_Default() {
		using var h = new Harness();
		// No default workspace at all — two siblings share the "orders-" prefix.
		h.AddWorkspace("alpha", new FilterRule("orders-", null));
		h.AddWorkspace("beta", new FilterRule("orders-", null));

		// alpha already embedded the event on stream "orders-1".
		var seeded = CountingGenerator.VectorFor("payload");
		h.Store("alpha", IndexKind.Events).Add(100, seeded);
		h.Filters("alpha").AddLogPosition(100);

		// beta processes the same event: its prefix matches "orders-1", so the cache probes
		// alpha and reuses the vector instead of regenerating — no default involved.
		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"beta", Ids(100), Streams("orders-1"), Kinds(IndexKind.Events), Txt("payload"), CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(seeded);
		await Assert.That(h.Generator.CallCount).IsEqualTo(0);
	}

	[Test]
	public async Task Does_Not_Probe_Sibling_Whose_Prefix_Does_Not_Match() {
		using var h = new Harness();
		h.AddWorkspace("alpha", new FilterRule("orders-", null));
		h.AddWorkspace("beta", new FilterRule("payments-", null));
		h.Store("alpha", IndexKind.Events).Add(100, CountingGenerator.VectorFor("payload"));
		h.Filters("alpha").AddLogPosition(100);

		// beta asks about a "payments-" stream: alpha's prefix doesn't match, so alpha isn't
		// probed and beta regenerates. (In practice alpha would never hold beta's docs.)
		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"beta", Ids(100), Streams("payments-1"), Kinds(IndexKind.Events), Txt("payload"), CancellationToken.None);

		await Assert.That(h.Store("alpha", IndexKind.Events).Probes).IsEqualTo(0); // prefix gate skipped alpha
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
	}

	[Test]
	public async Task Probes_At_Most_MaxWorkspaceProbes_Siblings() {
		// MaxWorkspaceProbes (5) caps the prefix-matching siblings probed per doc so an unusually
		// broad candidate set can't blow up lookup cost. Six siblings all claim position 100 in
		// their bloom filter but hold nothing (false positives), so every probe misses; the scan
		// must stop after 5 store probes and fall through to generation.
		using var h = new Harness();
		string[] siblings = ["s0", "s1", "s2", "s3", "s4", "s5"];
		foreach (var name in siblings) {
			h.AddWorkspace(name, new FilterRule("orders-", null));
			h.Filters(name).AddLogPosition(100);
		}

		// "req" is the requester (excluded from the sibling scan) and isn't registered.
		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"req", Ids(100), Streams("orders-1"), Kinds(IndexKind.Events), Txt("payload"), CancellationToken.None);

		var totalProbes = siblings.Sum(name => h.Store(name, IndexKind.Events).Probes);
		await Assert.That(totalProbes).IsEqualTo(5); // MaxWorkspaceProbes
		await Assert.That(result[0].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("payload"));
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
	}

	// --- The requester's own store (per IndexKind) ---

	[Test]
	public async Task Reuses_Stream_Vector_From_Requesters_Own_Store() {
		using var h = new Harness();
		h.AddWorkspace("alpha");

		// Stream names recur and the in-memory LRU evicts them, so a workspace re-stages its own
		// streams — the EventsStreams lookup probes the requester's own store to avoid re-embedding.
		var seeded = CountingGenerator.VectorFor("orders 1");
		h.Store("alpha", IndexKind.EventsStreams).Add(7777, seeded);
		h.Filters("alpha").AddStreamHash(7777);

		var result = await h.Cache.GetOrCreateStreamEmbeddingsAsync(
			"alpha", Ids(7777), Streams("orders-1"), Kinds(IndexKind.EventsStreams), Txt("orders 1"), CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(seeded);
		await Assert.That(h.Generator.CallCount).IsEqualTo(0);
	}

	[Test]
	public async Task Event_Doc_Does_Not_Reuse_From_Requesters_Own_Store() {
		using var h = new Harness();
		h.AddWorkspace("alpha");

		// alpha has position 100 in its own Events store, but event lookups don't probe the
		// requester (positions are unique; only a bounded checkpoint replay re-sees them), so
		// it regenerates rather than waste a probe.
		h.Store("alpha", IndexKind.Events).Add(100, CountingGenerator.VectorFor("payload"));
		h.Filters("alpha").AddLogPosition(100);

		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"alpha", Ids(100), Streams("orders-1"), Kinds(IndexKind.Events), Txt("payload"), CancellationToken.None);

		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
	}

	[Test]
	public async Task Memory_Doc_Skips_The_Cache() {
		using var h = new Harness();
		h.AddWorkspace("alpha");
		h.AddWorkspace("beta", new FilterRule("orders-", null));

		// The vector is present in both the requester's own Memory store and a prefix-matching
		// sibling, yet a Memory lookup skips the cache entirely — neither is consulted — so it
		// regenerates. (Memory docs are private and normally new; replay re-embeds are fine.)
		h.Store("alpha", IndexKind.Memory).Add(100, CountingGenerator.VectorFor("payload"));
		h.Filters("alpha").AddLogPosition(100);
		h.Store("beta", IndexKind.Memory).Add(100, CountingGenerator.VectorFor("payload"));
		h.Filters("beta").AddLogPosition(100);

		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"alpha", Ids(100), Streams("orders-1"), Kinds(IndexKind.Memory), Txt("payload"), CancellationToken.None);

		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
	}

	// --- Bloom-filter hint ---

	[Test]
	public async Task Bloom_Hint_Verifies_Against_Vector_Store_On_False_Positive() {
		using var h = new Harness();
		h.AddWorkspace(WorkspaceNaming.DefaultName);
		h.AddWorkspace("alpha", new FilterRule("orders-", null));

		// Default's bloom claims position 42, but its vector was never persisted (false positive).
		// alpha's lookup hits default's bloom, probes its store, misses, and falls through to generation.
		h.Filters(WorkspaceNaming.DefaultName).AddLogPosition(42);

		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"alpha", Ids(42), Streams("orders-1"), Kinds(IndexKind.Events), Txt("new"), CancellationToken.None);

		// The bloom hint led to a store read (so the hint isn't trusted blindly) that then missed.
		await Assert.That(h.Store(WorkspaceNaming.DefaultName, IndexKind.Events).Probes).IsEqualTo(1);
		await Assert.That(result[0].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("new"));
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
	}

	// --- Batching & coalescing ---

	[Test]
	public async Task Duplicate_DocId_In_Batch_Generates_Once() {
		using var h = new Harness();
		h.AddWorkspace("solo");

		// Same doc twice in one batch, nothing cached: the second occurrence coalesces onto
		// the in-flight entry created by the first, so the generator runs once.
		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"solo", Ids(500, 500), Streams("orders-1", "orders-1"), Kinds(IndexKind.Events, IndexKind.Events), Txt("z", "z"), CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("z"));
		await Assert.That(result[1].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("z"));
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
		await Assert.That(h.Generator.TextCount).IsEqualTo(1);
	}

	[Test]
	public async Task Mixed_Batch_Hits_And_Misses_Only_Generates_Misses() {
		using var h = new Harness();
		h.AddWorkspace(WorkspaceNaming.DefaultName);
		h.AddWorkspace("alpha", new FilterRule("orders-", null));

		// 100 is cached in default (which the requester alpha reuses); 200 and 300 are not.
		var cached = CountingGenerator.VectorFor("a");
		h.Store(WorkspaceNaming.DefaultName, IndexKind.Events).Add(100, cached);
		h.Filters(WorkspaceNaming.DefaultName).AddLogPosition(100);

		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"alpha",
			Ids(100, 200, 300),
			Streams("orders-1", "orders-2", "orders-3"),
			Kinds(IndexKind.Events, IndexKind.Events, IndexKind.Events),
			Txt("a", "b", "c"),
			CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(cached);
		await Assert.That(result[1].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("b"));
		await Assert.That(result[2].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("c"));
		// Single generator call covering only the two misses.
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
		await Assert.That(h.Generator.TextCount).IsEqualTo(2);
	}

	[Test]
	public async Task Concurrent_Requests_For_Same_DocId_Coalesce() {
		using var h = new Harness();
		h.AddWorkspace("a");
		h.AddWorkspace("b");

		// Block the generator so two requests overlap.
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var blockedGen = new BlockingGenerator(gate.Task);
		var cache = new EmbeddingCache(h.Workspaces, h.VectorStores, blockedGen, NullLoggerFactory.Instance);
		cache.MountBloomFilters("a", h.Filters("a"));
		cache.MountBloomFilters("b", h.Filters("b"));

		var taskA = cache.GetOrCreateEventEmbeddingsAsync("a", Ids(777), Streams("orders-1"), Kinds(IndexKind.Events), Txt("dup"), CancellationToken.None).AsTask();
		var taskB = cache.GetOrCreateEventEmbeddingsAsync("b", Ids(777), Streams("orders-1"), Kinds(IndexKind.Events), Txt("dup"), CancellationToken.None).AsTask();

		// Give them a moment to both reach the in-flight map.
		await Task.Delay(50);
		gate.SetResult(true);

		var resA = await taskA;
		var resB = await taskB;

		// Both got the same value, but generator was called once with one text.
		await Assert.That(resA[0].ToArray()).IsEquivalentTo(resB[0].ToArray());
		await Assert.That(blockedGen.CallCount).IsEqualTo(1);
		await Assert.That(blockedGen.TotalTextCount).IsEqualTo(1);
	}

	// --- Resilience ---

	[Test]
	public async Task Swallows_ObjectDisposedException_From_Other_Workspace_Store() {
		using var h = new Harness();
		h.AddWorkspace(WorkspaceNaming.DefaultName);
		h.AddWorkspace("alpha");

		// Default's bloom filter claims a hit for position 100, but its store is "disposed".
		h.Filters(WorkspaceNaming.DefaultName).AddLogPosition(100);
		h.Store(WorkspaceNaming.DefaultName, IndexKind.Events).DisposedThrowOnAccess = true;
		h.Store(WorkspaceNaming.DefaultName, IndexKind.Memory).DisposedThrowOnAccess = true;

		// Should not throw — should fall through to generation.
		var result = await h.Cache.GetOrCreateEventEmbeddingsAsync(
			"alpha", Ids(100), Streams("orders-1"), Kinds(IndexKind.Events), Txt("x"), CancellationToken.None);

		await Assert.That(result[0].ToArray()).IsEquivalentTo(CountingGenerator.VectorFor("x"));
		await Assert.That(h.Generator.CallCount).IsEqualTo(1);
	}

	sealed class BlockingGenerator(Task gate) : IEmbeddingGenerator<string, Embedding<float>> {
		public int CallCount;
		public int TotalTextCount;

		public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
			CancellationToken cancellationToken = default) {
			Interlocked.Increment(ref CallCount);
			await gate;
			var result = new GeneratedEmbeddings<Embedding<float>>();
			foreach (var v in values) {
				Interlocked.Increment(ref TotalTextCount);
				result.Add(new Embedding<float>(CountingGenerator.VectorFor(v)));
			}
			return result;
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }
	}
}

// The cache fills a caller-owned buffer; these adapters allocate one and return it so the tests
// can assert on a plain array. ValueTask keeps .AsTask() working at the call sites.
static class EmbeddingCacheTestExtensions {
	public static async ValueTask<ReadOnlyMemory<float>[]> GetOrCreateEventEmbeddingsAsync(
		this EmbeddingCache cache, string requester, ReadOnlyMemory<ulong> positions,
		ReadOnlyMemory<string> streams, ReadOnlyMemory<IndexKind> kinds,
		ReadOnlyMemory<string> texts, CancellationToken ct) {
		var results = new ReadOnlyMemory<float>[positions.Length];
		await cache.GetOrCreateEventEmbeddingsAsync(requester, positions, streams, kinds, texts, results, ct);
		return results;
	}

	public static async ValueTask<ReadOnlyMemory<float>[]> GetOrCreateStreamEmbeddingsAsync(
		this EmbeddingCache cache, string requester, ReadOnlyMemory<ulong> hashes,
		ReadOnlyMemory<string> streams, ReadOnlyMemory<IndexKind> kinds,
		ReadOnlyMemory<string> texts, CancellationToken ct) {
		var results = new ReadOnlyMemory<float>[hashes.Length];
		await cache.GetOrCreateStreamEmbeddingsAsync(requester, hashes, streams, kinds, texts, results, ct);
		return results;
	}
}
