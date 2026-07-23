// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class InquiryManagerTests {
	// -------- TTL --------

	[Test]
	public async Task Expired_Inquiry_Is_Not_Returned() {
		var inquiries = new InquiryManager(ttl: TimeSpan.Zero);
		var inquiry = inquiries.Create(WorkspaceNaming.DefaultName);

		await Task.Delay(1);
		await Assert.That(inquiries.Get(inquiry.Id, WorkspaceNaming.DefaultName)).IsNull();
	}

	[Test]
	public async Task Expired_Inquiries_Are_Evicted_On_Create() {
		var inquiries = new InquiryManager(ttl: TimeSpan.FromMilliseconds(50));
		inquiries.Create(WorkspaceNaming.DefaultName);

		await Task.Delay(100);
		inquiries.Create(WorkspaceNaming.DefaultName);

		await Assert.That(inquiries.ListAll()).Count().IsEqualTo(1);
	}

	[Test]
	public async Task Active_Inquiry_Is_Not_Expired() {
		var inquiries = new InquiryManager(ttl: TimeSpan.FromSeconds(10));
		var inquiry = inquiries.Create(WorkspaceNaming.DefaultName);

		await Assert.That(inquiries.Get(inquiry.Id, WorkspaceNaming.DefaultName)).IsNotNull();
	}

	// -------- Working-set isolation --------

	[Test]
	public async Task Inquiries_Have_Independent_Working_Sets() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100, 0.9f));
		var inquiries = new InquiryManager();
		var sid1 = inquiries.Create(WorkspaceNaming.DefaultName).Id;
		var sid2 = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid1, queries: ["order"]);
		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid2, queries: ["order"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(1);
	}
}
