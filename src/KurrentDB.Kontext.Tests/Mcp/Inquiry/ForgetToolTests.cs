// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class ForgetToolTests {
	[Test]
	public async Task Forget_Removes_From_Working_Set() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f)
			.AddSearchResult("order-1", 1, "OrderShipped", 101, 0.8f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100))
			.AddStreamEvent("order-1", MakeEvent("order-1", 1, "OrderShipped", 101));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		var result = ForgetTool.Forget(new FakeWorkspaceContext(), inquiries, sid,
			eventIds: [100]);

		await Assert.That(result.Forgotten).IsEqualTo(1);
		await Assert.That(result.WorkingSetSize).IsEqualTo(1);
	}

	[Test]
	public async Task Forget_Excludes_From_Future_Searches() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		ForgetTool.Forget(new FakeWorkspaceContext(), inquiries, sid,
			eventIds: [100]);

		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(0);
	}

	[Test]
	public async Task Forget_Multiple_Events() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f)
			.AddSearchResult("order-1", 1, "OrderShipped", 101, 0.8f)
			.AddSearchResult("order-1", 2, "OrderDelivered", 102, 0.7f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100))
			.AddStreamEvent("order-1", MakeEvent("order-1", 1, "OrderShipped", 101))
			.AddStreamEvent("order-1", MakeEvent("order-1", 2, "OrderDelivered", 102));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		var result = ForgetTool.Forget(new FakeWorkspaceContext(), inquiries, sid,
			eventIds: [100, 101]);

		await Assert.That(result.Forgotten).IsEqualTo(2);
		await Assert.That(result.WorkingSetSize).IsEqualTo(1);
	}
}
