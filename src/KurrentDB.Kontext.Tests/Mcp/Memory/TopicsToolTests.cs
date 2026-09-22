// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Mcp.Memory;

public class TopicsToolTests {
	static FakeWorkspaceContext Workspace(string name = WorkspaceNaming.DefaultName) => new(name);

	[Test]
	public async Task Returns_Topics_With_Latest_Fact() {
		var search = new FakeSearchService()
			.AddStreamHit("$kontext-memory:default:alice-current-employer", 0.9f)
			.AddStreamHit("$kontext-memory:default:alice-current-role", 0.6f);
		var client = new FakeSystemClient()
			.AddStreamEvent("$kontext-memory:default:alice-current-employer",
				FakeSystemClient.MakeEvent(
					stream: "$kontext-memory:default:alice-current-employer",
					eventNumber: 0, eventType: "FactRetained",
					data: new { fact = "Alice works at Acme.", retainedAt = "2026-05-01T09:00:00Z" }))
			.AddStreamEvent("$kontext-memory:default:alice-current-employer",
				FakeSystemClient.MakeEvent(
					stream: "$kontext-memory:default:alice-current-employer",
					eventNumber: 1, eventType: "FactRetained",
					data: new { fact = "Alice works at Globex.", retainedAt = "2026-05-15T09:00:00Z" }))
			.AddStreamEvent("$kontext-memory:default:alice-current-role",
				FakeSystemClient.MakeEvent(
					stream: "$kontext-memory:default:alice-current-role",
					eventNumber: 0, eventType: "FactRetained",
					data: new { fact = "Alice is a staff engineer.", retainedAt = "2026-05-10T09:00:00Z" }));

		var result = await TopicsTool.Topics(search, client, Workspace(), TestWorkspace.Registry(), keywords: "alice");

		await Assert.That(result.Topics).Count().IsEqualTo(2);
		await Assert.That(result.Topics[0].Topic).IsEqualTo("alice-current-employer");
		await Assert.That(result.Topics[0].Score).IsEqualTo(0.9f);
		await Assert.That(result.Topics[0].LatestFact).IsEqualTo("Alice works at Globex.");

		await Assert.That(result.Topics[1].Topic).IsEqualTo("alice-current-role");
		await Assert.That(result.Topics[1].LatestFact).IsEqualTo("Alice is a staff engineer.");
	}

	[Test]
	public async Task Lists_Topics_When_Keywords_Omitted() {
		// Browse mode: an argless call must not throw and should return the workspace's topics.
		var search = new FakeSearchService()
			.AddStreamHit("$kontext-memory:default:alice-current-employer", 0f)
			.AddStreamHit("$kontext-memory:default:bob-timezone", 0f);

		var result = await TopicsTool.Topics(search, new FakeSystemClient(), Workspace(), TestWorkspace.Registry(), keywords: null);

		await Assert.That(result.Topics.Select(t => t.Topic))
			.Contains("alice-current-employer").And.Contains("bob-timezone");
	}

	[Test]
	public async Task Skips_Streams_Outside_Workspace_Prefix() {
		var search = new FakeSearchService()
			.AddStreamHit("$kontext-memory:other-ws:foo", 0.9f);

		var result = await TopicsTool.Topics(search, new FakeSystemClient(), Workspace("default"), TestWorkspace.Registry(), keywords: "foo");

		await Assert.That(result.Topics).IsEmpty();
	}

	[Test]
	public async Task Empty_When_No_Stream_Hits() {
		var search = new FakeSearchService();
		var result = await TopicsTool.Topics(search, new FakeSystemClient(), Workspace(), TestWorkspace.Registry(), keywords: "anything");
		await Assert.That(result.Topics).IsEmpty();
	}

	[Test]
	public async Task Populates_RetainedAt_From_Latest_Event() {
		var search = new FakeSearchService()
			.AddStreamHit("$kontext-memory:default:alice-current-role", 0.9f);
		var retainedAt = new DateTime(2026, 5, 15, 9, 0, 0, DateTimeKind.Utc);
		var client = new FakeSystemClient()
			.AddStreamEvent("$kontext-memory:default:alice-current-role",
				FakeSystemClient.MakeEvent(
					stream: "$kontext-memory:default:alice-current-role",
					eventNumber: 0, eventType: "FactRetained",
					data: new { fact = "Staff engineer.", retainedAt = retainedAt.ToString("o") }));

		var result = await TopicsTool.Topics(search, client, Workspace(), TestWorkspace.Registry(), keywords: "alice");

		await Assert.That(result.Topics[0].RetainedAt.ToUniversalTime()).IsEqualTo(retainedAt);
	}

	[Test]
	public async Task Throws_When_Workspace_Not_Found() {
		var search = new FakeSearchService();
		var registry = new WorkspaceRegistry(); // empty — no workspaces registered

		await Assert.That(async () => await TopicsTool.Topics(
				search, new FakeSystemClient(), Workspace("ghost"), registry, keywords: "x"))
			.Throws<WorkspaceNotFoundException>();
	}

	[Test]
	public async Task Throws_When_Memory_Disabled() {
		var search = new FakeSearchService()
			.AddStreamHit("$kontext-memory:default:alice", 0.9f);
		var registry = TestWorkspace.Registry(disableMemory: true);

		await Assert.That(async () => await TopicsTool.Topics(
				search, new FakeSystemClient(), Workspace(), registry, keywords: "alice"))
			.Throws<WorkspaceOperationDisabledException>();
	}
}
