// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Mcp.Workspace;

public class StreamsToolTests {
	[Test]
	public async Task Returns_Matching_Streams() {
		var search = new FakeSearchService()
			.AddStreamHit("alice-hobbies", 0.9f)
			.AddStreamHit("alice-relationships", 0.7f);

		var result = await StreamsTool.Streams(search, new FakeWorkspaceContext(), keywords: "alice");

		await Assert.That(result.Streams).Count().IsEqualTo(2);
		await Assert.That(result.Streams[0].Name).IsEqualTo("alice-hobbies");
		await Assert.That(result.Streams[0].Score).IsEqualTo(0.9f);
	}

	[Test]
	public async Task Forwards_Keywords_To_Service() {
		var search = new FakeSearchService();

		await StreamsTool.Streams(search, new FakeWorkspaceContext(), keywords: "order-2024");

		await Assert.That(search.SearchQueries[0]).IsEqualTo("order-2024");
	}
}
