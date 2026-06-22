// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Indexing;

/// <summary>
/// Tests for SubscriptionBase's filter-rule matching: prefix-first-match-wins,
/// JS filter evaluation, and the "no rule matched" defensive throw.
/// </summary>
public class SubscriptionBaseTests {
	static WorkspaceEntry Workspace(params FilterRule[] rules) =>
		WorkspaceEntry.Create(
			name: "alpha",
			filterRules: rules,
			fullTextIndexingEnabled: true,
			semanticIndexingEnabled: true,
			disableMemory: false,
			disableImports: false,
			disableInquiries: false,
			readOnly: false);

	static TestSubscription Make(params FilterRule[] rules) {
		var workspace = Workspace(rules);
		var checkpointPath = Path.Combine(Path.GetTempPath(), $"kontext-subbase-{Guid.NewGuid():N}.cp");
		return new TestSubscription(null!, workspace, EventFilter.Unfiltered, checkpointPath);
	}

	[Test]
	public async Task No_Rules_Matches_Everything() {
		using var sub = Make();
		var evt = FakeSystemClient.MakeEvent("orders-1", eventNumber: 0, eventType: "OrderPlaced");
		await Assert.That(sub.MatchesFilterRules(evt)).IsTrue();
	}

	[Test]
	public async Task Prefix_Match_Without_Filter_Returns_True() {
		using var sub = Make(new FilterRule("orders-", null));
		var evt = FakeSystemClient.MakeEvent("orders-1", eventNumber: 0, eventType: "OrderPlaced");
		await Assert.That(sub.MatchesFilterRules(evt)).IsTrue();
	}

	[Test]
	public async Task First_Matching_Prefix_Wins() {
		// Both rules match the stream, but the first one is reached first and short-circuits.
		using var sub = Make(
			new FilterRule("orders-", null),
			new FilterRule("orders-2024-", "event => false")); // would reject, but never reached
		var evt = FakeSystemClient.MakeEvent("orders-2024-jan-1", eventNumber: 0, eventType: "OrderPlaced");
		await Assert.That(sub.MatchesFilterRules(evt)).IsTrue();
	}

	[Test]
	public async Task Prefix_Match_With_JS_Filter_True_Returns_True() {
		using var sub = Make(new FilterRule("orders-", "event => true"));
		var evt = FakeSystemClient.MakeEvent("orders-1", eventNumber: 0, eventType: "OrderPlaced",
			data: new { amount = 100 });
		await Assert.That(sub.MatchesFilterRules(evt)).IsTrue();
	}

	[Test]
	public async Task Prefix_Match_With_JS_Filter_False_Returns_False() {
		using var sub = Make(new FilterRule("orders-", "event => false"));
		var evt = FakeSystemClient.MakeEvent("orders-1", eventNumber: 0, eventType: "OrderPlaced",
			data: new { amount = 100 });
		await Assert.That(sub.MatchesFilterRules(evt)).IsFalse();
	}

	[Test]
	public async Task JS_Filter_Can_Inspect_Event_Payload() {
		using var sub = Make(new FilterRule("orders-", "event => event.value.amount > 50"));
		var pass = FakeSystemClient.MakeEvent("orders-1", 0, "OrderPlaced", data: new { amount = 100 });
		var fail = FakeSystemClient.MakeEvent("orders-2", 0, "OrderPlaced", data: new { amount = 25 });
		await Assert.That(sub.MatchesFilterRules(pass)).IsTrue();
		await Assert.That(sub.MatchesFilterRules(fail)).IsFalse();
	}

	[Test]
	public async Task No_Matching_Prefix_Throws() {
		using var sub = Make(new FilterRule("orders-", null));
		// 'payments-1' doesn't match the orders- prefix — should throw because the
		// server-side filter shouldn't have let this through.
		var evt = FakeSystemClient.MakeEvent("payments-1", eventNumber: 0, eventType: "PaymentMade");
		await Assert.That(() => sub.MatchesFilterRules(evt))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("no filter rule matched");
	}

	const long MaxLagBytes = 256 * 1024 * 1024; // mirrors SubscriptionBase.MaxCheckpointLagBytes

	[Test]
	public async Task FlushStore_Within_Lag_Bound_Stays_Lazy() {
		var store = new RecordingStore { Watermark = new TFPos(100, 100) };
		var current = new TFPos(100 + MaxLagBytes, 100 + MaxLagBytes); // exactly at the bound

		var watermark = TestSubscription.CallFlushStore(store, current);

		await Assert.That(watermark).IsEqualTo(new TFPos(100, 100));
		await Assert.That(store.Calls.SequenceEqual([false])).IsTrue(); // never forced
	}

	[Test]
	public async Task FlushStore_Beyond_Lag_Bound_Forces_Persistence() {
		var store = new RecordingStore { Watermark = new TFPos(100, 100) };
		var current = new TFPos(101 + MaxLagBytes, 101 + MaxLagBytes); // one byte past the bound

		var watermark = TestSubscription.CallFlushStore(store, current);

		await Assert.That(watermark).IsEqualTo(current); // forced flush persisted everything
		await Assert.That(store.Calls.SequenceEqual([false, true])).IsTrue(); // lazy, then forced
	}

	sealed class RecordingStore : IIndexStore {
		public TFPos Watermark;
		public List<bool> Calls { get; } = [];

		public TFPos Flush(TFPos current, bool force) {
			Calls.Add(force);
			return force ? current : Watermark;
		}
	}

	sealed class TestSubscription : SubscriptionBase {
		public TestSubscription(ISystemClient client, WorkspaceEntry workspace, IEventFilter filter, string checkpointPath)
			: base(client, workspace, filter, checkpointPath) { _path = checkpointPath; }

		public static TFPos CallFlushStore(IIndexStore store, TFPos current) => FlushStore(store, current);

		readonly string _path;

		protected override string PipelineName => "Test";
		protected override ILogger Logger => NullLogger.Instance;
		protected override void OnCaughtUp() { }
		protected override TFPos FlushIndexes(TFPos current, bool disposing) => current;
		protected override void UpdatePosition(ulong commitPosition) { }
		protected override void RecordIndexed(DateTime eventTimestamp) { }
		protected override KontextProgressTracker.Scope StartIndexScope() => _tracker.StartFtsIndex();
		protected override KontextProgressTracker.Scope StartCommitScope() => _tracker.StartFtsCommit();
		protected override int BatchCount => 0;
		protected override bool IsBatchFull => false;
		protected override ValueTask StageEventAsync(ResolvedEvent evt, IndexKind targetKind, CancellationToken ct) => default;
		protected override Task IndexBatchAsync(CancellationToken ct) => Task.CompletedTask;

		static readonly KontextProgressTracker _tracker = new("test", new IndexingStatus(), new Meter("test"), () => 0);

		public override void Dispose() {
			base.Dispose();
			File.Delete(_path);
		}
	}
}
