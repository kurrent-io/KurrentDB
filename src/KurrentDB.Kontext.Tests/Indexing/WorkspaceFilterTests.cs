// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces.ControlPlane;
using Microsoft.Extensions.Logging.Abstractions;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Indexing;

public class WorkspaceFilterTests {
	static FilterResult Eval(WorkspaceFilter filter, string stream, object data) =>
		filter.Evaluate(MakeEvent(stream, 0, "SomeEvent", data: data).OriginalEvent, NullLogger.Instance);

	[Test]
	public async Task No_Rules_Matches_Everything() {
		var filter = WorkspaceFilter.Compile([]);
		await Assert.That(Eval(filter, "anything", new { org = "acme" })).IsEqualTo(FilterResult.Match);
	}

	[Test]
	public async Task Prefix_Without_Filter_Matches() {
		var filter = WorkspaceFilter.Compile([new FilterRule("order-", null)]);
		await Assert.That(Eval(filter, "order-1", new { org = "acme" })).IsEqualTo(FilterResult.Match);
	}

	[Test]
	public async Task Non_Matching_Prefix_Is_OutOfScope() {
		var filter = WorkspaceFilter.Compile([new FilterRule("order-", null)]);
		await Assert.That(Eval(filter, "payment-1", new { org = "acme" })).IsEqualTo(FilterResult.OutOfScope);
	}

	[Test]
	public async Task Js_Filter_Accepts_And_Excludes_By_Content() {
		var filter = WorkspaceFilter.Compile([new FilterRule("ticket-", "rec => rec.value.org === 'acme'")]);
		await Assert.That(Eval(filter, "ticket-1", new { org = "acme" })).IsEqualTo(FilterResult.Match);
		await Assert.That(Eval(filter, "ticket-2", new { org = "globex" })).IsEqualTo(FilterResult.Excluded);
	}

	[Test]
	public async Task First_Matching_Prefix_Wins() {
		// The broad rule (no filter) is reached first and short-circuits, so the narrower
		// rule's filter never runs.
		var filter = WorkspaceFilter.Compile([
			new FilterRule("ticket-", null),
			new FilterRule("ticket-2024-", "rec => false"),
		]);
		await Assert.That(Eval(filter, "ticket-2024-1", new { org = "acme" })).IsEqualTo(FilterResult.Match);
	}

	[Test]
	public async Task Js_Filter_Error_Excludes_The_Event() {
		// The filter dereferences a missing field; a thrown error must exclude, not match.
		var filter = WorkspaceFilter.Compile([new FilterRule("ticket-", "rec => rec.value.missing.deep === 1")]);
		await Assert.That(Eval(filter, "ticket-1", new { org = "acme" })).IsEqualTo(FilterResult.Excluded);
	}
}
