// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

public class USearchVectorStoreTests : IDisposable {
	readonly string _embeddingsPath;
	readonly VectorIndexMetadata _meta;

	public USearchVectorStoreTests() {
		var root = Path.Combine(Path.GetTempPath(), $"kurrent_kontext_vec_test_{Guid.NewGuid():N}");
		_embeddingsPath = Path.Combine(root, "embeddings");
		Directory.CreateDirectory(_embeddingsPath);
		_meta = new VectorIndexMetadata("Test", "test-model", EmbeddingService.Dimensions);
	}

	public void Dispose() {
		var root = Path.GetDirectoryName(_embeddingsPath);
		if (root != null && Directory.Exists(root))
			Directory.Delete(root, recursive: true);
	}

	USearchVectorStore New(int? sealThreshold = null) =>
		sealThreshold.HasValue
			? new USearchVectorStore(_embeddingsPath, "test", _meta,
				NullLogger<USearchVectorStore>.Instance, sealThreshold: sealThreshold.Value)
			: new USearchVectorStore(_embeddingsPath, "test", _meta,
				NullLogger<USearchVectorStore>.Instance);

	static float[] MakeEmbedding(int seed) {
		var rng = new Random(seed);
		var emb = new float[EmbeddingService.Dimensions];
		for (var i = 0; i < emb.Length; i++)
			emb[i] = (float)(rng.NextDouble() * 2 - 1);
		var norm = MathF.Sqrt(emb.Sum(x => x * x));
		if (norm > 0)
			for (var i = 0; i < emb.Length; i++)
				emb[i] /= norm;
		return emb;
	}

	[Test]
	public async Task Add_Then_Search_Finds_Vector() {
		using var store = New();
		var emb = MakeEmbedding(1);
		store.Add(1, emb);

		var hits = store.Search(emb, limit: 10).ToList();
		await Assert.That(hits).Contains(1ul);
	}

	[Test]
	public async Task Search_Empty_Index_Returns_Empty() {
		using var store = New();
		var hits = store.Search(MakeEmbedding(1), limit: 10).ToList();
		await Assert.That(hits).IsEmpty();
	}

	[Test]
	public async Task TryGet_Returns_Stored_Vector() {
		using var store = New();
		var emb = MakeEmbedding(1);
		store.Add(1, emb);

		await Assert.That(store.TryGet(1, out var stored)).IsTrue();
		await Assert.That(stored.Length).IsEqualTo(emb.Length);
	}

	[Test]
	public async Task TryGet_Missing_Id_Returns_False() {
		using var store = New();
		await Assert.That(store.TryGet(42, out _)).IsFalse();
	}

	[Test]
	public async Task Constructor_Throws_When_EmbeddingModel_Mismatches() {
		// Seed a segment with the original model.
		using (var store = New()) {
			store.Add(1, MakeEmbedding(1));
			store.Flush();
		}

		var differentMeta = new VectorIndexMetadata("Test", "other-model", EmbeddingService.Dimensions);
		await Assert.That(() => new USearchVectorStore(
				_embeddingsPath, "test", differentMeta, NullLogger<USearchVectorStore>.Instance))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("model");
	}

	[Test]
	public async Task Search_Hits_Both_The_Buffer_And_Sealed_Segments() {
		using var store = New();
		var emb = MakeEmbedding(1);

		store.Add(1, emb);
		store.Flush(); // doc 1 → sealed into a segment
		store.Add(2, emb); // doc 2 stays in the in-memory L0 buffer

		var hits = store.Search(emb, limit: 10).ToList();
		await Assert.That(hits).Contains(1ul);
		await Assert.That(hits).Contains(2ul);
	}

	[Test]
	public async Task Recovery_Probe_Skips_Vectors_Already_In_A_Segment() {
		// Seed a segment, then restart the store. The new instance should probe the segments on
		// Add(1, emb) and treat it as a duplicate rather than re-inserting.
		var emb = MakeEmbedding(1);
		using (var seed = New()) {
			seed.Add(1, emb);
			seed.Flush();
		}

		using var store = New();
		store.Add(1, emb); // recovery probe should skip
		var hits = store.Search(emb, limit: 10).ToList();
		await Assert.That(hits.Count(id => id == 1ul)).IsEqualTo(1);
	}

	[Test]
	public async Task Multiple_Merge_Cycles_Preserve_All_Vectors() {
		using var store = New();
		var emb = MakeEmbedding(1);

		store.Add(1, emb);
		store.Flush();
		store.Add(2, emb);
		store.Flush();
		store.Add(3, emb);
		store.Flush();

		var hits = store.Search(emb, limit: 10).ToList();
		await Assert.That(hits).Contains(1ul);
		await Assert.That(hits).Contains(2ul);
		await Assert.That(hits).Contains(3ul);
	}

	[Test]
	public async Task Buffer_Reaching_Seal_Threshold_Seals_A_Segment() {
		using var store = New(sealThreshold: 3);
		var emb = MakeEmbedding(1);

		store.Add(1, emb);
		store.Add(2, emb);
		await Assert.That(SegmentFiles()).IsEmpty();

		store.Add(3, emb); // reaches threshold → seals an L0 segment
		await Assert.That(SegmentFiles()).IsNotEmpty();
		await Assert.That(File.Exists(Path.Combine(StoreDir, "manifest.usearchmap"))).IsTrue();
	}

	string StoreDir => Path.Combine(_embeddingsPath, "test.usearch");
	string[] SegmentFiles() => Directory.Exists(StoreDir) ? Directory.GetFiles(StoreDir, "*.usearch") : [];

	[Test]
	public async Task Level_Merge_Consolidates_Segments() {
		// sealThreshold 1 → one segment per Add. MaxSegmentsPerLevel is 4, so the 5th
		// level-0 segment triggers a background merge into a single level-1 segment.
		using var store = New(sealThreshold: 1);
		for (var i = 1; i <= 5; i++)
			store.Add((ulong)i, MakeEmbedding(i));

		// Wait for the background merge to consolidate the level-0 segments.
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (SegmentFiles().Length > MaxSegmentsPerLevelForTest && DateTime.UtcNow < deadline)
			await Task.Delay(25);

		await Assert.That(SegmentFiles().Length).IsLessThanOrEqualTo(MaxSegmentsPerLevelForTest);

		// Every vector survives the merge and is still findable.
		for (var i = 1; i <= 5; i++) {
			var hits = store.Search(MakeEmbedding(i), limit: 10).ToList();
			await Assert.That(hits).Contains((ulong)i);
		}
	}

	const int MaxSegmentsPerLevelForTest = 4;

	[Test]
	public async Task Concurrent_Search_During_Add_Does_Not_Crash() {
		// Regression: searching while the writer adds must never touch a USearch index
		// mid-mutation (the original native access-violation).
		using var store = New(sealThreshold: 64);
		var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		var added = 0;

		var writer = Task.Run(() => {
			var i = 0;
			while (!cts.Token.IsCancellationRequested) {
				store.Add((ulong)i, MakeEmbedding(i + 1));
				i++;
			}
			Interlocked.Exchange(ref added, i);
		});

		var searchers = Enumerable.Range(0, 4).Select(n => Task.Run(() => {
			while (!cts.Token.IsCancellationRequested)
				_ = store.Search(MakeEmbedding(1), limit: 10).ToList();
		})).ToArray();

		await Task.WhenAll([writer, .. searchers]);

		// Survived the concurrent add/search burst; the store is still consistent.
		await Assert.That(added).IsGreaterThan(0);
		await Assert.That(store.Search(MakeEmbedding(1), limit: 10).ToList()).IsNotEmpty();
	}

	static TFPos Pos(long p) => new(p, p);

	[Test]
	public async Task Flush_Returns_Current_When_Nothing_Is_Pending() {
		using var store = New();
		await Assert.That(store.Flush(Pos(500), force: false)).IsEqualTo(Pos(500));
	}

	[Test]
	public async Task Flush_Returns_Last_Sealed_Watermark_While_Buffer_Is_Pending() {
		using var store = New(sealThreshold: 2);
		store.Add(1, Pos(10), MakeEmbedding(1));
		store.Add(2, Pos(20), MakeEmbedding(2)); // reaches threshold → seals; durable up to 20
		store.Add(3, Pos(30), MakeEmbedding(3)); // pending in the buffer

		await Assert.That(store.Flush(Pos(40), force: false)).IsEqualTo(Pos(20));
	}

	[Test]
	public async Task Flush_With_Force_Seals_Pending_And_Returns_Current() {
		using var store = New();
		store.Add(1, Pos(10), MakeEmbedding(1)); // pending, below threshold

		await Assert.That(store.Flush(Pos(40), force: true)).IsEqualTo(Pos(40));
		await Assert.That(SegmentFiles()).IsNotEmpty(); // the pending vector was sealed

		// And the buffer being empty now, a lazy flush no longer constrains the checkpoint.
		await Assert.That(store.Flush(Pos(50), force: false)).IsEqualTo(Pos(50));
	}

	[Test]
	public async Task Empty_Flush_Advances_The_Watermark() {
		using var store = New();
		store.Flush(Pos(50), force: false); // empty → durable up to 50

		store.Add(1, Pos(60), MakeEmbedding(1)); // pending

		// The pending buffer constrains to the last known-durable position — 50, not (0,0).
		await Assert.That(store.Flush(Pos(70), force: false)).IsEqualTo(Pos(50));
	}

	[Test]
	public async Task Lazy_Flush_Does_Not_Seal() {
		using var store = New();
		store.Add(1, Pos(10), MakeEmbedding(1));

		store.Flush(Pos(40), force: false);

		await Assert.That(SegmentFiles()).IsEmpty(); // still buffered in memory
		await Assert.That(store.Search(MakeEmbedding(1), limit: 10).ToList()).Contains(1ul);
	}
}