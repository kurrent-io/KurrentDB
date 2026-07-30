// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Mcp.Memory;

// Recall ranks topics via the search service but presents each topic's CURRENT fact, read
// from the client — so the fact content/sources/timestamp are seeded on the client, while the
// search result supplies the topic (stream), the matched event number, and the score.
public class RecallToolTests {
	static FakeWorkspaceContext Workspace(string name = WorkspaceNaming.DefaultName) => new(name);

	static FakeSystemClient Fact(FakeSystemClient client, string stream, long eventNumber, object? data) {
		client.AddStreamEvent(stream, FakeSystemClient.MakeEvent(stream, eventNumber, "FactRetained", data: data));
		return client;
	}

	[Test]
	public async Task Returns_Structured_Fact() {
		const string stream = "$kontext-memory:default:alice-hobbies";
		var search = new FakeSearchService()
			.AddRecallResult(stream, eventNumber: 0, score: 0.9f, eventType: "FactRetained");
		var client = Fact(new FakeSystemClient(), stream, 0, new {
			fact = "Alice has a dog named Max.",
			sourceEvents = new[] {
				new { stream = "alice-pets", eventNumber = 3L },
				new { stream = "alice-pets", eventNumber = 7L },
			},
			retainedAt = "2026-05-19T12:00:00Z",
		});

		var result = await RecallTool.Recall(search, client, Workspace(), TestWorkspace.Registry(), "alice dog");

		await Assert.That(result.Count).IsEqualTo(1);
		var fact = result.Facts[0];
		await Assert.That(fact.Topic).IsEqualTo("alice-hobbies");
		await Assert.That(fact.Fact).IsEqualTo("Alice has a dog named Max.");
		await Assert.That(fact.SourceEvents).Count().IsEqualTo(2);
		await Assert.That(fact.SourceEvents[0].Stream).IsEqualTo("alice-pets");
		await Assert.That(fact.SourceEvents[0].EventNumber).IsEqualTo(3L);
		await Assert.That(fact.Superseded).IsFalse();
		await Assert.That(fact.Score).IsEqualTo(0.9f);
	}

	[Test]
	public async Task Returns_Empty_When_No_Hits() {
		var result = await RecallTool.Recall(
			new FakeSearchService(), new FakeSystemClient(), Workspace(), TestWorkspace.Registry(), "nonexistent");

		await Assert.That(result.Count).IsEqualTo(0);
		await Assert.That(result.Facts).Count().IsEqualTo(0);
	}

	[Test]
	public async Task Skips_Hits_Outside_Workspace_Memory_Prefix() {
		var search = new FakeSearchService()
			.AddRecallResult("$kontext-memory:other-ws:something", eventNumber: 0, score: 0.5f, eventType: "FactRetained");

		var result = await RecallTool.Recall(
			search, new FakeSystemClient(), Workspace("default"), TestWorkspace.Registry(), "anything");

		await Assert.That(result.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Throws_When_Memory_Disabled() {
		var registry = TestWorkspace.Registry(disableMemory: true);

		await Assert.That(async () => await RecallTool.Recall(
				new FakeSearchService(), new FakeSystemClient(), Workspace(), registry, "anything"))
			.Throws<WorkspaceOperationDisabledException>();
	}

	[Test]
	public async Task Throws_When_Workspace_Not_Found() {
		await Assert.That(async () => await RecallTool.Recall(
				new FakeSearchService(), new FakeSystemClient(), Workspace("ghost"), new WorkspaceRegistry(), "anything"))
			.Throws<WorkspaceNotFoundException>();
	}

	[Test]
	public async Task Populates_RetainedAt() {
		const string stream = "$kontext-memory:default:alice-hobbies";
		var retainedAt = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
		var search = new FakeSearchService()
			.AddRecallResult(stream, eventNumber: 0, score: 0.9f, eventType: "FactRetained");
		var client = Fact(new FakeSystemClient(), stream, 0, new { fact = "F", retainedAt = retainedAt.ToString("o") });

		var result = await RecallTool.Recall(search, client, Workspace(), TestWorkspace.Registry(), "alice");

		await Assert.That(result.Facts[0].RetainedAt.ToUniversalTime()).IsEqualTo(retainedAt);
	}

	[Test]
	public async Task Score_Rounded_To_Four_Decimals() {
		const string stream = "$kontext-memory:default:alice-hobbies";
		var search = new FakeSearchService()
			.AddRecallResult(stream, eventNumber: 0, score: 0.123456789f, eventType: "FactRetained");
		var client = Fact(new FakeSystemClient(), stream, 0, new { fact = "F" });

		var result = await RecallTool.Recall(search, client, Workspace(), TestWorkspace.Registry(), "alice");

		await Assert.That(result.Facts[0].Score).IsEqualTo(MathF.Round(0.123456789f, 4));
	}

	[Test]
	public async Task Multiple_Facts_Returned_In_Service_Order() {
		const string employer = "$kontext-memory:default:alice-current-employer";
		const string role = "$kontext-memory:default:alice-current-role";
		var search = new FakeSearchService()
			.AddRecallResult(employer, eventNumber: 0, score: 0.9f, eventType: "FactRetained")
			.AddRecallResult(role, eventNumber: 0, score: 0.6f, eventType: "FactRetained");
		var client = new FakeSystemClient();
		Fact(client, employer, 0, new { fact = "At Globex" });
		Fact(client, role, 0, new { fact = "Staff engineer" });

		var result = await RecallTool.Recall(search, client, Workspace(), TestWorkspace.Registry(), "alice");

		await Assert.That(result.Count).IsEqualTo(2);
		await Assert.That(result.Facts[0].Topic).IsEqualTo("alice-current-employer");
		await Assert.That(result.Facts[1].Topic).IsEqualTo("alice-current-role");
	}

	[Test]
	public async Task Presents_The_Current_Fact_And_Flags_Superseded_When_The_Match_Was_Stale() {
		const string stream = "$kontext-memory:default:alice-current-employer";
		// The body search matched the older version (event 0); event 1 is the current fact.
		var search = new FakeSearchService()
			.AddRecallResult(stream, eventNumber: 0, score: 0.9f, eventType: "FactRetained");
		var client = new FakeSystemClient();
		Fact(client, stream, 0, new { fact = "Alice works at Acme." });
		Fact(client, stream, 1, new { fact = "Alice works at Globex." });

		var result = await RecallTool.Recall(search, client, Workspace(), TestWorkspace.Registry(), "alice employer");

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result.Facts[0].Fact).IsEqualTo("Alice works at Globex.");
		await Assert.That(result.Facts[0].Superseded).IsTrue();
	}

	[Test]
	public async Task Dedups_Repeated_Topic_To_A_Single_Current_Fact() {
		const string stream = "$kontext-memory:default:alice-current-employer";
		// Both surviving versions of one topic matched; recall must collapse them to one.
		var search = new FakeSearchService()
			.AddRecallResult(stream, eventNumber: 1, score: 0.9f, eventType: "FactRetained")
			.AddRecallResult(stream, eventNumber: 0, score: 0.8f, eventType: "FactRetained");
		var client = new FakeSystemClient();
		Fact(client, stream, 0, new { fact = "old" });
		Fact(client, stream, 1, new { fact = "current" });

		var result = await RecallTool.Recall(search, client, Workspace(), TestWorkspace.Registry(), "alice");

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result.Facts[0].Fact).IsEqualTo("current");
		await Assert.That(result.Facts[0].Superseded).IsFalse(); // top-ranked match (event 1) is current
	}

	[Test]
	public async Task Skips_Topic_With_No_Current_Fact() {
		// A stale body-index entry matched, but the topic stream has no current event.
		var search = new FakeSearchService()
			.AddRecallResult("$kontext-memory:default:alice-gone", eventNumber: 0, score: 0.5f, eventType: "FactRetained");

		var result = await RecallTool.Recall(
			search, new FakeSystemClient(), Workspace(), TestWorkspace.Registry(), "anything");

		await Assert.That(result.Count).IsEqualTo(0);
	}
}
