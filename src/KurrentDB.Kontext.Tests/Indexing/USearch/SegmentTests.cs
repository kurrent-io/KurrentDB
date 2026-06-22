// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

public class SegmentTests : IDisposable {
	readonly string _dir;

	public SegmentTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"kontext_segment_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
	}

	public void Dispose() {
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	Segment Build(int level, params ulong[] ids) {
		var vecs = ids.Select(id => TestVectors.Embedding((int)id)).ToArray();
		return Segment.Build(_dir, TestVectors.Dims, ids, vecs, level);
	}

	[Test]
	public async Task Build_Writes_Segment_And_Sidecar() {
		using var segment = Build(0, 1, 2, 3);

		await Assert.That(segment.Size).IsEqualTo(3ul);
		await Assert.That(File.Exists(segment.Path)).IsTrue();
		await Assert.That(File.Exists(segment.Path + ".keys")).IsTrue();
	}

	[Test]
	public async Task Reopen_From_Disk_Finds_Vectors() {
		string path;
		using (var built = Build(0, 1, 2, 3))
			path = built.Path;

		using var reopened = new Segment(path, level: 0);
		await Assert.That(reopened.Size).IsEqualTo(3ul);
		await Assert.That(reopened.Contains(2)).IsTrue();
		await Assert.That(reopened.Contains(99)).IsFalse();
		await Assert.That(reopened.TryGet(1, out var vector)).IsTrue();
		await Assert.That(vector.Length).IsEqualTo(TestVectors.Dims);
	}

	[Test]
	public async Task Merge_Combines_All_Vectors_At_Target_Level() {
		using var a = Build(0, 1, 2);
		using var b = Build(0, 3, 4);

		using var merged = Segment.Merge(_dir, [a, b], level: 1, CancellationToken.None);

		await Assert.That(merged.Level).IsEqualTo(1);
		await Assert.That(merged.Size).IsEqualTo(4ul);
		foreach (var id in (ulong[])[1, 2, 3, 4])
			await Assert.That(merged.Contains(id)).IsTrue();
	}

	[Test]
	public async Task Merge_Deduplicates_Overlapping_Keys() {
		using var a = Build(0, 1, 2, 3);
		using var b = Build(0, 3, 4); // 3 overlaps a

		using var merged = Segment.Merge(_dir, [a, b], level: 1, CancellationToken.None);

		await Assert.That(merged.Size).IsEqualTo(4ul); // 1,2,3,4 — the shared 3 appears once
	}

	[Test]
	public async Task Merge_Preserves_The_Largest_Segments_Vectors() {
		using var small = Build(0, 1);
		using var large = Build(0, 10, 11, 12, 13); // largest → loaded as the base

		using var merged = Segment.Merge(_dir, [small, large], level: 1, CancellationToken.None);

		await Assert.That(merged.Size).IsEqualTo(5ul);
		foreach (var id in (ulong[])[1, 10, 11, 12, 13])
			await Assert.That(merged.Contains(id)).IsTrue();
	}

	[Test]
	public async Task Merge_Cancelled_Throws_And_Leaves_No_Temp_Files() {
		using var a = Build(0, 1, 2);
		using var b = Build(0, 3, 4);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.That(() => Segment.Merge(_dir, [a, b], level: 1, cts.Token))
			.Throws<OperationCanceledException>();

		await Assert.That(Directory.GetFiles(_dir, "*.tmp")).IsEmpty();
	}
}
