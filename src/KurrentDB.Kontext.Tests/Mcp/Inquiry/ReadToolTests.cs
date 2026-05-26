// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class ReadToolTests {
	[Test]
	public async Task Read_Single_Event() {
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "order-1", EventNumber = 0 });

		await Assert.That(result.NewEvents).HasCount().EqualTo(1);
		await Assert.That(result.NewEvents[0].EventType).IsEqualTo("OrderPlaced");
	}

	[Test]
	public async Task Read_Range() {
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100))
			.AddStreamEvent("order-1", MakeEvent("order-1", 1, "OrderShipped", 101))
			.AddStreamEvent("order-1", MakeEvent("order-1", 2, "OrderDelivered", 102));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "order-1", EventNumber = 0, Count = 3 });

		await Assert.That(result.NewEvents).HasCount().EqualTo(3);
	}

	[Test]
	public async Task Read_Already_Seen_Silently_Skipped() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "order-1", EventNumber = 0 });

		await Assert.That(result.NewEvents).HasCount().EqualTo(0);
		await Assert.That(result.WorkingSetSize).IsEqualTo(1);
	}

	[Test]
	public async Task Read_Skips_Forgotten_Event() {
		var search = new FakeSearchService()
			.AddSearchResult("order-1", 0, "OrderPlaced", 100, 0.9f);
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		await SearchTool.Search(search, new FakeWorkspaceContext(), new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid, queries: ["order"]);
		ForgetTool.Forget(new FakeWorkspaceContext(), inquiries, sid,
			eventIds: [100]);
		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "order-1", EventNumber = 0 });

		await Assert.That(result.NewEvents).HasCount().EqualTo(0);
		await Assert.That(result.WorkingSetSize).IsEqualTo(0);
	}

	[Test]
	public async Task Read_EndOfStream_True_When_Range_Exceeds_Stream() {
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "order-1", EventNumber = 0, Count = 6 });

		await Assert.That(result.EndOfStream).IsTrue();
		await Assert.That(result.NewEvents).HasCount().EqualTo(1);
	}

	[Test]
	public async Task Read_EndOfStream_False_When_Range_Fully_Satisfied() {
		var reader = new FakeSystemClient()
			.AddStreamEvent("order-1", MakeEvent("order-1", 0, "OrderPlaced", 100))
			.AddStreamEvent("order-1", MakeEvent("order-1", 1, "OrderShipped", 101))
			.AddStreamEvent("order-1", MakeEvent("order-1", 2, "OrderDelivered", 102));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "order-1", EventNumber = 0, Count = 2 });

		await Assert.That(result.EndOfStream).IsFalse();
		await Assert.That(result.NewEvents).HasCount().EqualTo(2);
	}

	[Test]
	public async Task Read_EndOfStream_True_When_Stream_Doesnt_Exist() {
		var reader = new FakeSystemClient();
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "nothing-here", EventNumber = 0 });

		await Assert.That(result.EndOfStream).IsTrue();
		await Assert.That(result.NewEvents).HasCount().EqualTo(0);
	}

	[Test]
	public async Task Read_Throws_When_Stream_Outside_Workspace_Scope() {
		var reader = new FakeSystemClient()
			.AddStreamEvent("payments-1", MakeEvent("payments-1", 0, "Paid", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;
		// Workspace only indexes 'order-' streams; 'payments-1' is out of scope.
		var workspaces = TestWorkspace.Registry(filterRules: [new FilterRule("order-", null)]);

		await Assert.That(async () => {
			await ReadTool.Read(reader, new FakeWorkspaceContext(), workspaces, new FakeAuthorizationProvider(), new StaticHttpContextAccessor(), inquiries, sid,
				eventRef: new EventRef { Stream = "payments-1", EventNumber = 0 });
		})
			.Throws<StreamAccessDeniedException>();
	}

	[Test]
	public async Task Read_Throws_When_Stream_Not_Authorized() {
		var reader = new FakeSystemClient()
			.AddStreamEvent("secret-1", MakeEvent("secret-1", 0, "Locked", 100));
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;
		var authz = new FakeAuthorizationProvider().Deny("secret-1");

		await Assert.That(async () => {
			await ReadTool.Read(reader, new FakeWorkspaceContext(), TestWorkspace.Registry(),authz, new StaticHttpContextAccessor(), inquiries, sid,
				eventRef: new EventRef { Stream = "secret-1", EventNumber = 0 });
		})
			.Throws<StreamAccessDeniedException>();
	}

	[Test]
	public async Task Read_Applies_The_Workspace_Js_Filter_Per_Event() {
		// The stream's prefix is in scope, but the workspace's JS filter scopes it to org 'acme'.
		// inq_read must drop the non-acme event, matching the indexer (otherwise the JS scope leaks).
		var reader = new FakeSystemClient()
			.AddStreamEvent("ticket-1", MakeEvent("ticket-1", 0, "TicketOpened", 100, data: new { org = "acme" }))
			.AddStreamEvent("ticket-1", MakeEvent("ticket-1", 1, "TicketOpened", 101, data: new { org = "globex" }));
		var registry = TestWorkspace.Registry(
			filterRules: [new FilterRule("ticket-", "rec => rec.value.org === 'acme'")]);
		var inquiries = new InquiryManager();
		var sid = inquiries.Create(WorkspaceNaming.DefaultName).Id;

		var result = await ReadTool.Read(reader, new FakeWorkspaceContext(), registry, new FakeAuthorizationProvider(),
			new StaticHttpContextAccessor(), inquiries, sid,
			eventRef: new EventRef { Stream = "ticket-1", EventNumber = 0, Count = 2 });

		await Assert.That(result.NewEvents).HasCount().EqualTo(1);
		await Assert.That(result.NewEvents[0].Id).IsEqualTo(100L);
	}
}