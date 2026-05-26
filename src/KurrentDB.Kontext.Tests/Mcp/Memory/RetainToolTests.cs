// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Tests.Mcp.Memory;

public class RetainToolTests {
	static FakeWorkspaceContext Workspace(string name = WorkspaceNaming.DefaultName) => new(name);

	[Test]
	public async Task Writes_To_Topic_Stream() {
		var client = new FakeSystemClient();
		var facts = new[]
		{
			new RetainedFact
			{
				Topic = "alice-hobbies",
				Fact = "Alice has a dog named Max.",
				Keywords = ["pet owner", "animal", "canine"],
				SourceEvents = [new SourceEvent("alice-pets", 3), new SourceEvent("alice-pets", 7)],
			},
		};

		var result = await RetainTool.Retain(client, Workspace(), TestWorkspace.Registry(), facts);

		await Assert.That(client.WrittenEvents).Count().IsEqualTo(1);
		await Assert.That(client.WrittenEvents[0].Stream).IsEqualTo("$kontext-memory:default:alice-hobbies");
		var written = JsonDocument.Parse(client.WrittenEvents[0].Data).RootElement;
		await Assert.That(written.GetProperty("fact").GetString()).IsEqualTo("Alice has a dog named Max.");
		await Assert.That(written.GetProperty("keywords").GetArrayLength()).IsEqualTo(3);
		var firstSource = written.GetProperty("sourceEvents")[0];
		await Assert.That(firstSource.GetProperty("stream").GetString()).IsEqualTo("alice-pets");
		await Assert.That(firstSource.GetProperty("eventNumber").GetInt64()).IsEqualTo(3L);

		await Assert.That(result.Retained).IsEqualTo(1);
	}

	[Test]
	public async Task Throws_On_Empty_Fact() {
		var client = new FakeSystemClient();
		var facts = new[]
		{
			new RetainedFact { Topic = "t", Fact = "Valid fact" },
			new RetainedFact { Topic = "t", Fact = "" },
		};

		await Assert.That(async () => await RetainTool.Retain(client, Workspace(), TestWorkspace.Registry(), facts))
			.Throws<ToolInputException>();
		await Assert.That(client.WrittenEvents).IsEmpty();
	}

	[Test]
	public async Task Throws_On_Invalid_Topic() {
		var client = new FakeSystemClient();
		var facts = new[]
		{
			new RetainedFact { Topic = "bad topic/here", Fact = "Valid fact text." },
		};

		await Assert.That(async () => await RetainTool.Retain(client, Workspace(), TestWorkspace.Registry(), facts))
			.Throws<ToolInputException>();
		await Assert.That(client.WrittenEvents).IsEmpty();
	}

	[Test]
	public async Task Throws_On_Fact_Exceeding_MaxLength() {
		var client = new FakeSystemClient();
		var facts = new[]
		{
			new RetainedFact { Topic = "t", Fact = new string('x', RetainTool.MaxFactLength + 1) },
		};

		await Assert.That(async () => await RetainTool.Retain(client, Workspace(), TestWorkspace.Registry(), facts))
			.Throws<ToolInputException>();
		await Assert.That(client.WrittenEvents).IsEmpty();
	}

	[Test]
	public async Task Throws_When_Memory_Disabled() {
		var client = new FakeSystemClient();
		var registry = TestWorkspace.Registry(disableMemory: true);
		var facts = new[] { new RetainedFact { Topic = "t", Fact = "Alice climbs." } };

		await Assert.That(async () => await RetainTool.Retain(client, Workspace(), registry, facts))
			.Throws<WorkspaceOperationDisabledException>();
		await Assert.That(client.WrittenEvents).IsEmpty();
	}

	[Test]
	public async Task Throws_When_ReadOnly() {
		var client = new FakeSystemClient();
		var registry = TestWorkspace.Registry(readOnly: true);
		var facts = new[] { new RetainedFact { Topic = "t", Fact = "Alice climbs." } };

		await Assert.That(async () => await RetainTool.Retain(client, Workspace(), registry, facts))
			.Throws<WorkspaceOperationDisabledException>();
		await Assert.That(client.WrittenEvents).IsEmpty();
	}
}