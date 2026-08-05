// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Kontext.Workspaces;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class SearchToolTests {
	[Test]
	public async Task Search_Invalid_Inquiry_Throws() {
		var search = new FakeSearchService();
		var reader = new FakeSystemClient();
		var inquiries = new InquiryManager();

		await Assert.That(async () => { await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, "nope", queries: ["test"]); })
			.Throws<InquiryNotFoundException>();
	}

	[Test]
	public async Task Search_Returns_New_Events() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.95f,
				data: new { orderId = "order-1", product = "Widget" });
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(1);
		await Assert.That(result.NewEvents[0].Stream).IsEqualTo("order-1");
		await Assert.That(result.NewEvents[0].EventType).IsEqualTo("OrderPlaced");
		await Assert.That(result.NewEvents[0].Score).IsEqualTo(0.95f);
		await Assert.That(JsonDocument.Parse(result.NewEvents[0].Data).RootElement.GetProperty("product").GetString()).IsEqualTo("Widget");
		await Assert.That(result.Searches[0].New).IsEqualTo(1);
	}

	[Test]
	public async Task Search_Excludes_Already_Seen_Events() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(0);
		await Assert.That(result.Searches[0].New).IsEqualTo(0);
	}

	[Test]
	public async Task Search_Multiple_Queries() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f)
			.AddSearchResult("payment-1", 0, "PaymentReceived", 200, 0.8f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100))
			.AddStreamEvent("payment-1", MakeEvent("payment-1", 0, "PaymentReceived", 200));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order", "payment"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(2);
	}

	[Test]
	public async Task Search_Deduplicates_Across_Queries() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order", "placed"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(1);
	}

	[Test]
	public async Task Search_No_Results() {
		var search = new FakeSearchService();
		var reader = new FakeSystemClient();
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["nonexistent"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(0);
	}

	[Test]
	public async Task Search_Skips_Blank_Queries() {
		var search = new FakeSearchService();
		var reader = new FakeSystemClient();
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["", "  "]);

		await Assert.That(search.SearchQueries).IsEmpty();
	}

	[Test]
	public async Task Search_New_Events_Sorted_By_Score_Descending() {
		var search = new FakeSearchService()
			.AddSearchResult("a-1", 0, "LowScore", 100, 0.3f)
			.AddSearchResult("b-1", 0, "HighScore", 200, 0.9f)
			.AddSearchResult("c-1", 0, "MidScore", 300, 0.6f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("a-1", MakeEvent("a-1", 0, "LowScore", 100, 0.3f))
			.AddStreamEvent("b-1", MakeEvent("b-1", 0, "HighScore", 200, 0.9f))
			.AddStreamEvent("c-1", MakeEvent("c-1", 0, "MidScore", 300, 0.6f));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["score"]);

		await Assert.That(result.NewEvents[0].EventType).IsEqualTo("HighScore");
		await Assert.That(result.NewEvents[1].EventType).IsEqualTo("MidScore");
		await Assert.That(result.NewEvents[2].EventType).IsEqualTo("LowScore");
	}

	[Test]
	public async Task Search_Redacts_Events_From_Unauthorized_Streams() {
		var search = new FakeSearchService()
			.AddSearchResult("public-1", 0, "PublicEvent", 100, 0.9f, data: new { msg = "hello" })
			.AddSearchResult("secret-1", 0, "SecretEvent", 200, 0.8f, data: new { msg = "top secret" });
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var authz = new FakeAuthorizationProvider().Deny("secret-1");
		var result = await SearchTool.Search(search, new FakeWorkspaceContext(), authz,
			new StaticHttpContextAccessor(), inquiries, sid, queries: ["any"]);

		await Assert.That(result.NewEvents).Count().IsEqualTo(2);
		var publicEvt = result.NewEvents.Single(e => e.Stream == "public-1");
		var secretEvt = result.NewEvents.Single(e => e.Stream == "secret-1");

		await Assert.That(JsonDocument.Parse(publicEvt.Data).RootElement.GetProperty("msg").GetString()).IsEqualTo("hello");
		await Assert.That(secretEvt.Data.IsEmpty).IsTrue();
	}
}
