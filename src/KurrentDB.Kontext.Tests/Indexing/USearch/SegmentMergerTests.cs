// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

public class SegmentMergerTests : IDisposable {
	readonly string _dir;
	readonly string _manifest;

	public SegmentMergerTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"kontext_merger_{Guid.NewGuid():N}");
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
	public async Task Trigger_Consolidates_An_Overflowing_Level() {
		using var segments = new Segments(_dir, _manifest);
		for (ulong i = 0; i < 5; i++) // 5 > MaxSegmentsPerLevel (4)
			segments.Add(Seg(0, i));

		using var merger = new SegmentMerger(segments, _dir, NullLogger.Instance, "test");
		merger.Trigger();

		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (segments.Count > 4 && DateTime.UtcNow < deadline)
			await Task.Delay(25);

		await Assert.That(segments.Count).IsLessThanOrEqualTo(4);
		for (ulong i = 0; i < 5; i++)
			await Assert.That(segments.Contains(i)).IsTrue();
	}

	[Test]
	public async Task Trigger_Is_NoOp_When_Nothing_To_Merge() {
		using var segments = new Segments(_dir, _manifest);
		segments.Add(Seg(0, 1));
		segments.Add(Seg(0, 2));

		using var merger = new SegmentMerger(segments, _dir, NullLogger.Instance, "test");
		merger.Trigger();
		await Task.Delay(200);

		await Assert.That(segments.Count).IsEqualTo(2); // below threshold — untouched
	}

	[Test]
	public async Task Dispose_Stops_Merging() {
		using var segments = new Segments(_dir, _manifest);
		for (ulong i = 0; i < 5; i++)
			segments.Add(Seg(0, i));

		var merger = new SegmentMerger(segments, _dir, NullLogger.Instance, "test");
		merger.Trigger();
		merger.Dispose(); // cancels and waits for the in-flight merge to unwind

		// Whatever state we land in, the segment set is consistent and all ids are still present.
		for (ulong i = 0; i < 5; i++)
			await Assert.That(segments.Contains(i)).IsTrue();
	}
}
