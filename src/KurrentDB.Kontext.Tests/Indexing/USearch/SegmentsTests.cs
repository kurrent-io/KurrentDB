// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

public class SegmentsTests : IDisposable {
	readonly string _dir;
	readonly string _manifest;

	public SegmentsTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"kontext_segments_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
		_manifest = Path.Combine(_dir, "manifest.usearchmap");
	}

	public void Dispose() {
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	Segment Seg(int level, params ulong[] ids) =>
		Segment.Build(_dir, TestVectors.Dims, ids, ids.Select(id => TestVectors.Embedding((int)id)).ToArray(), level);

	[Test]
	public async Task Add_Then_Contains_TryGet_And_Search() {
		using var segments = new Segments(_dir, _manifest);
		segments.Add(Seg(0, 1, 2));

		await Assert.That(segments.Count).IsEqualTo(1);
		await Assert.That(segments.Contains(1)).IsTrue();
		await Assert.That(segments.Contains(99)).IsFalse();
		await Assert.That(segments.TryGet(2, out _)).IsTrue();

		var matches = new NearestMatches();
		segments.Search(TestVectors.Embedding(1), limit: 10, matches);
		await Assert.That(matches.Nearest(10)).Contains(1ul);
	}

	[Test]
	public async Task FindMergeGroup_Null_At_Threshold_Group_Above() {
		using var segments = new Segments(_dir, _manifest);
		for (ulong i = 0; i < 4; i++) // 4 == MaxSegmentsPerLevel, not yet overflowing
			segments.Add(Seg(0, i));
		await Assert.That(segments.FindMergeGroup()).IsNull();

		segments.Add(Seg(0, 100)); // 5th level-0 segment → overflow
		var group = segments.FindMergeGroup();
		await Assert.That(group).IsNotNull();
		await Assert.That(group!.Value.TargetLevel).IsEqualTo(1);
		await Assert.That(group.Value.Group).Count().IsEqualTo(5);
	}

	[Test]
	public async Task Replace_Swaps_Group_For_Merged_And_Deletes_Originals() {
		using var segments = new Segments(_dir, _manifest);
		var a = Seg(0, 1);
		var b = Seg(0, 2);
		segments.Add(a);
		segments.Add(b);

		var merged = Seg(1, 1, 2);
		segments.Replace([a, b], merged);

		await Assert.That(segments.Count).IsEqualTo(1);
		await Assert.That(segments.Contains(1)).IsTrue();
		await Assert.That(segments.Contains(2)).IsTrue();
		await Assert.That(File.Exists(a.Path)).IsFalse();
		await Assert.That(File.Exists(b.Path)).IsFalse();
	}

	[Test]
	public async Task Manifest_Persists_Across_Reload() {
		using (var segments = new Segments(_dir, _manifest)) {
			segments.Add(Seg(0, 1, 2));
			segments.Add(Seg(1, 3));
		}

		using var reloaded = new Segments(_dir, _manifest);
		await Assert.That(reloaded.Count).IsEqualTo(2);
		await Assert.That(reloaded.TotalVectors).IsEqualTo(3);
		await Assert.That(reloaded.Contains(2)).IsTrue();
		await Assert.That(reloaded.Contains(3)).IsTrue();
	}

	[Test]
	public async Task Reads_After_Dispose_Return_Empty_Without_Throwing() {
		var segments = new Segments(_dir, _manifest);
		segments.Add(Seg(0, 1));
		segments.Dispose();

		await Assert.That(segments.Count).IsEqualTo(0);
		await Assert.That(segments.Contains(1)).IsFalse();
		await Assert.That(segments.TryGet(1, out _)).IsFalse();
	}

	[Test]
	public async Task Add_After_Dispose_Is_Dropped() {
		var segments = new Segments(_dir, _manifest);
		segments.Dispose();

		segments.Add(Seg(0, 1)); // guarded — dropped, not resurrected
		await Assert.That(segments.Count).IsEqualTo(0);
	}
}
