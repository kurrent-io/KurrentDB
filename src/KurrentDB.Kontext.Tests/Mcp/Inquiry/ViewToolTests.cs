// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class ViewToolTests {
	[Test]
	public async Task View_Empty_Inquiry() {
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ViewTool.View(inquiries, new FakeWorkspaceContext(), sid);

		await Assert.That(result.WorkingSetSize).IsEqualTo(0);
		await Assert.That(result.Events).Count().IsEqualTo(0);
	}

	[Test]
	public async Task View_Shows_All_Events_Chronologically() {
		var search = new FakeSearchService()
			.AddSearchResult("b-1", 0, "Second", 200, 0.8f)
			.AddSearchResult("a-1", 0, "First", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("b-1", MakeEvent("b-1", 0, "Second", 200, 0.8f))
			.AddStreamEvent("a-1", MakeEvent("a-1", 0, "First", 100, 0.9f));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["first second"]);
		var result = await ViewTool.View(inquiries, new FakeWorkspaceContext(), sid);

		await Assert.That(result.WorkingSetSize).IsEqualTo(2);
		await Assert.That(result.Events[0].EventType).IsEqualTo("First");
		await Assert.That(result.Events[1].EventType).IsEqualTo("Second");
	}

	[Test]
	public async Task View_Invalid_Inquiry_Throws() {
		var inquiries = new InquiryManager();

		await Assert.That(async () => { await ViewTool.View(inquiries, new FakeWorkspaceContext(), "nope"); })
			.Throws<InquiryNotFoundException>();
	}

	[Test]
	public async Task View_After_Forget_Excludes_Forgotten() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f)
			.AddSearchResult("order-1", 1, "OrderShipped", 101, 0.8f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100, 0.9f))
			.AddStreamEvent("order-1", MakeEvent("order-1", 1, "OrderShipped", 101, 0.8f));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		ForgetTool.Forget(new FakeWorkspaceContext(), inquiries, sid,
			eventIds: [100]);

		var result = await ViewTool.View(inquiries, new FakeWorkspaceContext(), sid);

		await Assert.That(result.WorkingSetSize).IsEqualTo(1);
		await Assert.That(result.Events[0].EventType).IsEqualTo("OrderShipped");
	}
}
