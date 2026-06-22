// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Kontext.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace KurrentDB.Kontext.Tests.Diagnostics;

public class KontextProgressTrackerTests {
	const string WorkspaceName = "alpha";

	sealed record Measurement(string Name, double Value, IReadOnlyDictionary<string, string?> Tags);

	sealed class Capture : IDisposable {
		readonly MeterListener _listener;
		public List<Measurement> Measurements { get; } = new();

		public Capture(Meter meter) {
			_listener = new MeterListener {
				InstrumentPublished = (instrument, l) => {
					if (instrument.Meter == meter)
						l.EnableMeasurementEvents(instrument);
				}
			};
			_listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
				Measurements.Add(new(inst.Name, value, TagsToDict(tags))));
			_listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
				Measurements.Add(new(inst.Name, value, TagsToDict(tags))));
			_listener.Start();
		}

		public void RecordObservables() => _listener.RecordObservableInstruments();

		public void Dispose() => _listener.Dispose();

		static Dictionary<string, string?> TagsToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags) {
			var d = new Dictionary<string, string?>();
			foreach (var t in tags)
				d[t.Key] = t.Value as string;
			return d;
		}
	}

	static (KontextProgressTracker Tracker, IndexingStatus Status, Capture Capture, FakeTimeProvider Clock) Make(long writerPos = 0) {
		var meter = new Meter($"test-{Guid.NewGuid():N}");
		var status = new IndexingStatus();
		var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var tracker = new KontextProgressTracker(WorkspaceName, status, meter, () => writerPos, clock: clock);
		return (tracker, status, new Capture(meter), clock);
	}

	[Test]
	public async Task Gap_Reports_Bytes_Per_Pipeline() {
		var (_, status, capture, _) = Make(writerPos: 1000);
		using var _capture = capture;

		status.UpdateFtsPosition(700);
		status.UpdateEmbeddingPosition(400);

		capture.RecordObservables();

		var fts = capture.Measurements.Single(m => m.Name.EndsWith(".gap") && m.Tags["pipeline"] == "fts");
		var emb = capture.Measurements.Single(m => m.Name.EndsWith(".gap") && m.Tags["pipeline"] == "embedding");
		await Assert.That(fts.Value).IsEqualTo(300);
		await Assert.That(emb.Value).IsEqualTo(600);
		await Assert.That(fts.Tags["workspace"]).IsEqualTo(WorkspaceName);
	}

	[Test]
	public async Task Gap_Skips_When_Position_Not_Yet_Reported() {
		var (_, status, capture, _) = Make(writerPos: 1000);
		using var _capture = capture;

		status.UpdateFtsPosition(700);
		// embedding never advanced

		capture.RecordObservables();

		var gaps = capture.Measurements.Where(m => m.Name.EndsWith(".gap")).ToList();
		await Assert.That(gaps.Count).IsEqualTo(1);
		await Assert.That(gaps[0].Tags["pipeline"]).IsEqualTo("fts");
	}

	[Test]
	public async Task Gap_Skips_When_Writer_Position_Is_Zero() {
		var (_, status, capture, _) = Make(writerPos: 0);
		using var _capture = capture;

		status.UpdateFtsPosition(700);

		capture.RecordObservables();

		await Assert.That(capture.Measurements.Any(m => m.Name.EndsWith(".gap"))).IsFalse();
	}

	[Test]
	public async Task Lag_Reports_Seconds_Since_Last_Indexed_Event() {
		var (_, status, capture, clock) = Make(writerPos: 1);
		using var _capture = capture;

		var eventTime = clock.GetUtcNow().UtcDateTime;
		status.RecordFtsIndexed(eventTime);
		status.RecordEmbeddingIndexed(eventTime);

		clock.Advance(TimeSpan.FromSeconds(15));
		capture.RecordObservables();

		var fts = capture.Measurements.Single(m => m.Name.EndsWith(".lag") && m.Tags["pipeline"] == "fts");
		var emb = capture.Measurements.Single(m => m.Name.EndsWith(".lag") && m.Tags["pipeline"] == "embedding");
		await Assert.That(fts.Value).IsEqualTo(15);
		await Assert.That(emb.Value).IsEqualTo(15);
	}

	[Test]
	public async Task Lag_Reports_Zero_When_Event_Has_Future_Timestamp() {
		var (_, status, capture, clock) = Make(writerPos: 1);
		using var _capture = capture;

		status.RecordFtsIndexed(clock.GetUtcNow().UtcDateTime.AddSeconds(60));
		capture.RecordObservables();

		var fts = capture.Measurements.Single(m => m.Name.EndsWith(".lag"));
		await Assert.That(fts.Value).IsEqualTo(0); // clamped at 0 — no negative lag
	}

	[Test]
	public async Task Lag_Skips_When_No_Event_Indexed_Yet() {
		var (_, _, capture, _) = Make(writerPos: 1);
		using var _capture = capture;

		capture.RecordObservables();
		await Assert.That(capture.Measurements.Any(m => m.Name.EndsWith(".lag"))).IsFalse();
	}

	[Test]
	public async Task Commit_Histogram_Records_Elapsed_With_Fts_Tag() {
		var (tracker, _, capture, clock) = Make();
		using var _capture = capture;

		using (tracker.StartFtsCommit())
			clock.Advance(TimeSpan.FromMilliseconds(250));

		var commit = capture.Measurements.Single(m => m.Name.EndsWith(".commit.seconds"));
		await Assert.That(commit.Tags["pipeline"]).IsEqualTo("fts");
		await Assert.That(commit.Tags["workspace"]).IsEqualTo(WorkspaceName);
		await Assert.That(commit.Value).IsEqualTo(0.25);
	}

	[Test]
	public async Task Commit_Histogram_Distinguishes_Fts_And_Embedding() {
		var (tracker, _, capture, clock) = Make();
		using var _capture = capture;

		using (tracker.StartFtsCommit())
			clock.Advance(TimeSpan.FromMilliseconds(100));
		using (tracker.StartEmbeddingCommit())
			clock.Advance(TimeSpan.FromMilliseconds(300));

		var commits = capture.Measurements.Where(m => m.Name.EndsWith(".commit.seconds")).ToList();
		await Assert.That(commits.Count).IsEqualTo(2);
		await Assert.That(commits[0].Tags["pipeline"]).IsEqualTo("fts");
		await Assert.That(commits[1].Tags["pipeline"]).IsEqualTo("embedding");
	}

	[Test]
	public async Task Index_Histogram_Records_Separately_From_Commit() {
		var (tracker, _, capture, clock) = Make();
		using var _capture = capture;

		using (tracker.StartFtsIndex())
			clock.Advance(TimeSpan.FromMilliseconds(700));
		using (tracker.StartFtsCommit())
			clock.Advance(TimeSpan.FromMilliseconds(50));

		var index = capture.Measurements.Single(m => m.Name.EndsWith(".index.seconds"));
		var commit = capture.Measurements.Single(m => m.Name.EndsWith(".commit.seconds"));
		await Assert.That(index.Value).IsEqualTo(0.7);
		await Assert.That(commit.Value).IsEqualTo(0.05);
		await Assert.That(index.Tags["pipeline"]).IsEqualTo("fts");
		await Assert.That(commit.Tags["pipeline"]).IsEqualTo("fts");
	}

	[Test]
	public async Task Metrics_Are_Tagged_With_Workspace_Name() {
		var (tracker, status, capture, _) = Make(writerPos: 1000);
		using var _capture = capture;

		status.UpdateFtsPosition(500);
		status.RecordFtsIndexed(DateTime.UtcNow);
		using (tracker.StartFtsCommit()) { }

		capture.RecordObservables();

		await Assert.That(capture.Measurements.All(m => m.Tags["workspace"] == WorkspaceName)).IsTrue();
	}
}
