// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Text.Json;
using EventStore.Plugins;
using EventStore.Plugins.Authorization;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Kontext;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Mcp.Memory;
using KurrentDB.Kontext.Mcp.Inquiry;
using KurrentDB.Kontext.Mcp.Workspace;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Plugins.Kontext.Tests;

/// <summary>
/// End-to-end tests that exercise the real Kontext indexing pipeline (Lucene + USearch + ONNX)
/// against an in-process KurrentDB node. Each test gets its own data directory + test stream so
/// state stays isolated even though the underlying node is shared.
/// </summary>
public class IntegrationTests {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	ISystemClient SystemClient => NodeShim.Node.Services.GetRequiredService<ISystemClient>();

	IHost _host = null!;
	IKontextService _kontextService = null!;
	ISystemClient _client = null!;
	InquiryManager _inquiries = null!;
	WorkspaceRegistry _workspaces = null!;
	WorkspaceContext _workspaceContext = null!;
	IAuthorizationProvider _authz = null!;
	IHttpContextAccessor _httpAccessor = null!;
	string _testStream = null!;
	string _dataPath = null!;

	[Before(Test)]
	public async Task Setup(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		_testStream = $"test-{testId}";
		_dataPath = Path.Combine(Path.GetTempPath(), $"kontext-integ-{testId}");

		// Seed test events on the shared node before the host starts so the FTS subscription
		// picks them up during catchup.
		await SystemClient.Writing.WriteEvents(_testStream, [
			new Event(Guid.NewGuid(), "OrderPlaced", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { product = "Widget", customer = "Alice", amount = 42 })),
			new Event(Guid.NewGuid(), "OrderShipped", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { product = "Widget", carrier = "FedEx" })),
			new Event(Guid.NewGuid(), "OrderDelivered", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { product = "Widget", signedBy = "Alice" })),
		], requireLeader: false, principal: SystemAccounts.System, cancellationToken: ct);

		var node = NodeShim.Node.Services;
		var builder = Host.CreateApplicationBuilder();
		builder.Services.AddSingleton(SystemClient);
		builder.Services.AddSingleton(node.GetRequiredService<IIndexBackend<string>>());
		builder.Services.AddSingleton(node.GetRequiredService<IReadIndex<string>>());
		builder.Services.AddSingleton(node.GetRequiredService<Eventuous.IEventStore>());
		builder.Services.AddSingleton(node.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>());
		builder.Services.AddSingleton(node.GetRequiredService<Kurrent.Surge.Schema.ISchemaRegistry>());
		builder.Services.AddSingleton(new KontextStorageConfig { DataPath = _dataPath });
		builder.Services.AddSingleton<IAuthorizationProvider>(new AllowAllAuthorizationProvider());
		builder.Services.AddSingleton<IHttpContextAccessor>(new TestHttpContextAccessor());
		builder.Services.AddSingleton<InquiryManager>();
		var writerCheckpoint = node.GetRequiredService<TFChunkDbConfig>().WriterCheckpoint;
		builder.Services.AddSingleton<GetWriterCheckpoint>(() => writerCheckpoint.Read());
		builder.Services.AddKontext();

		_host = builder.Build();

		// Create and start the default workspace before the host (and its projection) starts, so the
		// $kontext-workspaces stream exists when the projection subscribes — otherwise it sees
		// StreamNotFound and stops. The live host creates it in KontextPlugin.Start().
		await _host.Services.InitializeKontextAsync(ct);
		await _host.Services.GetRequiredService<WorkspaceCommandService>()
			.Handle(new StartWorkspaceRequest(WorkspaceNaming.DefaultName), ct);

		await _host.StartAsync(ct);

		_kontextService = _host.Services.GetRequiredService<IKontextService>();
		_client = _host.Services.GetRequiredService<ISystemClient>();
		_inquiries = _host.Services.GetRequiredService<InquiryManager>();
		_workspaces = _host.Services.GetRequiredService<WorkspaceRegistry>();
		_workspaceContext = new FixedWorkspaceContext();
		_authz = _host.Services.GetRequiredService<IAuthorizationProvider>();
		_httpAccessor = _host.Services.GetRequiredService<IHttpContextAccessor>();

		// Wait for the FTS subscription to catch up to the seeded events. WaitFor swallows
		// the WorkspaceNotFoundException that StatusTool throws before the projection
		// hydrates the registry, so the same poll handles both projection catch-up and
		// FTS catch-up.
		await WaitFor(async () => {
			var status = await StatusTool.GetStatus(_client, _workspaces, _workspaceContext, ct);
			return status.FullTextIndexing.State == StatusTool.PipelineState.CaughtUp;
		}, ct);
	}

	[After(Test)]
	public async Task Teardown(CancellationToken ct) {
		if (_host != null) {
			await _host.StopAsync(ct);
			_host.Dispose();
		}
		try { if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true); } catch { }
	}

	static async Task WaitFor(Func<Task<bool>> condition, CancellationToken ct, int maxAttempts = 60, int delayMs = 500) {
		for (var i = 0; i < maxAttempts; i++) {
			ct.ThrowIfCancellationRequested();
			try { if (await condition()) return; } catch { /* not ready yet */ }
			await Task.Delay(delayMs, ct);
		}
		throw new TimeoutException("Indexing did not catch up in time.");
	}

	[Test, Category("Integration")]
	public async Task Inquiry_Create_Then_End_Removes_It() {
		var create = NewInquiryTool.NewInquiry(_inquiries, _workspaceContext, _workspaces);
		await Assert.That(_inquiries.Get(create.InquiryId, WorkspaceNaming.DefaultName)).IsNotNull();

		EndInquiryTool.EndInquiry(_inquiries, _workspaceContext, create.InquiryId);
		await Assert.That(_inquiries.Get(create.InquiryId, WorkspaceNaming.DefaultName)).IsNull();
	}

	[Test, Category("Integration")]
	public async Task Search_Finds_Indexed_Events() {
		var sid = NewInquiryTool.NewInquiry(_inquiries, _workspaceContext, _workspaces).InquiryId;

		var result = await SearchTool.Search(_kontextService, _workspaceContext, _authz, _httpAccessor, _inquiries,
			sid, queries: ["Widget Alice"], streamFilter: _testStream);

		await Assert.That(result.NewEvents.Length).IsGreaterThan(0);
		await Assert.That(result.NewEvents.Any(e => e.EventType == "OrderPlaced")).IsTrue();
	}

	[Test, Category("Integration")]
	public async Task Read_Fetches_Specific_Event() {
		var sid = NewInquiryTool.NewInquiry(_inquiries, _workspaceContext, _workspaces).InquiryId;

		var result = await ReadTool.Read(_client, _workspaceContext, _workspaces, _authz, _httpAccessor, _inquiries, sid,
			new EventRef { Stream = _testStream, EventNumber = 0, Count = 1 });

		await Assert.That(result.NewEvents).HasCount().EqualTo(1);
		await Assert.That(result.NewEvents[0].EventType).IsEqualTo("OrderPlaced");
	}

	[Test, Category("Integration")]
	public async Task Forget_Drops_Event_From_Working_Set() {
		var sid = NewInquiryTool.NewInquiry(_inquiries, _workspaceContext, _workspaces).InquiryId;

		var read = await ReadTool.Read(_client, _workspaceContext, _workspaces, _authz, _httpAccessor, _inquiries, sid,
			new EventRef { Stream = _testStream, EventNumber = 0, Count = 3 });
		await Assert.That(read.NewEvents).HasCount().EqualTo(3);

		var forget = ForgetTool.Forget(_workspaceContext, _inquiries, sid, [read.NewEvents[0].Id]);
		await Assert.That(forget.Forgotten).IsEqualTo(1);
		await Assert.That(forget.WorkingSetSize).IsEqualTo(2);
	}

	[Test, Category("Integration")]
	public async Task View_Lists_Working_Set_In_Chronological_Order() {
		var sid = NewInquiryTool.NewInquiry(_inquiries, _workspaceContext, _workspaces).InquiryId;
		await ReadTool.Read(_client, _workspaceContext, _workspaces, _authz, _httpAccessor, _inquiries, sid,
			new EventRef { Stream = _testStream, EventNumber = 0, Count = 3 });

		var view = await ViewTool.View(_inquiries, _workspaceContext, sid);

		await Assert.That(view.WorkingSetSize).IsEqualTo(3);
		await Assert.That(view.Events[0].EventType).IsEqualTo("OrderPlaced");
		await Assert.That(view.Events[1].EventType).IsEqualTo("OrderShipped");
		await Assert.That(view.Events[2].EventType).IsEqualTo("OrderDelivered");
	}

	[Test, Category("Integration")]
	public async Task Retain_And_Recall_RoundTrip(CancellationToken ct) {
		var topic = $"alice-orders-{Guid.NewGuid():N}"[..16];
		var retain = await RetainTool.Retain(_client, _workspaceContext, _workspaces, [
			new RetainedFact {
				Topic = topic,
				Fact = $"Alice orders Widgets via FedEx ({_testStream}).",
				SourceEvents = [new SourceEvent(_testStream, 0), new SourceEvent(_testStream, 1)],
			},
		]);
		await Assert.That(retain.Retained).IsEqualTo(1);

		RecallResult? recalled = null;
		await WaitFor(async () => {
			recalled = await RecallTool.Recall(_kontextService, _client, _workspaceContext, _workspaces, $"Alice Widgets FedEx {_testStream}");
			return recalled.Facts.Any(f => f.Topic == topic);
		}, ct);

		var fact = recalled!.Facts.First(f => f.Topic == topic);
		await Assert.That(fact.Fact).Contains("Alice orders Widgets");
		await Assert.That(fact.SourceEvents).HasCount().EqualTo(2);
		await Assert.That(fact.SourceEvents[0].Stream).IsEqualTo(_testStream);
		await Assert.That(fact.SourceEvents[0].EventNumber).IsEqualTo(0L);
	}

	[Test, Category("Integration")]
	public async Task Topics_Surfaces_Latest_Fact(CancellationToken ct) {
		var topic = $"alice-current-employer-{Guid.NewGuid():N}"[..24];
		await RetainTool.Retain(_client, _workspaceContext, _workspaces, [
			new RetainedFact { Topic = topic, Fact = "Alice works at Acme." },
		]);
		await RetainTool.Retain(_client, _workspaceContext, _workspaces, [
			new RetainedFact { Topic = topic, Fact = "Alice works at Globex." },
		]);

		TopicsResult? topics = null;
		await WaitFor(async () => {
			topics = await TopicsTool.Topics(_kontextService, _client, _workspaceContext, _workspaces, keywords: "alice employer");
			return topics.Topics.Any(t => t.Topic == topic);
		}, ct);

		var hit = topics!.Topics.First(t => t.Topic == topic);
		await Assert.That(hit.LatestFact).IsEqualTo("Alice works at Globex.");
	}

	[Test, Category("Integration")]
	public async Task Recall_Finds_Fact_By_Topic_When_Body_Does_Not_Match(CancellationToken ct) {
		var suffix = Guid.NewGuid().ToString("N")[..8];

		// The query words live only in the TOPIC; the body deliberately shares none of them.
		// Body-only recall would miss this; the topic search is what surfaces it.
		var target = $"acme-quarterly-revenue-{suffix}";
		await RetainTool.Retain(_client, _workspaceContext, _workspaces, [
			new RetainedFact { Topic = target, Fact = "The figure landed at 4.2 million." },
		]);

		// Distractors whose bodies are unrelated to the query, so the target isn't trivially
		// the only thing the body/vector search could return.
		await RetainTool.Retain(_client, _workspaceContext, _workspaces, [
			new RetainedFact { Topic = $"bob-hobbies-{suffix}", Fact = "He enjoys hiking and weekend chess tournaments." },
			new RetainedFact { Topic = $"carol-commute-{suffix}", Fact = "She cycles to the office along the river path." },
		]);

		RecallResult? recalled = null;
		await WaitFor(async () => {
			recalled = await RecallTool.Recall(_kontextService, _client, _workspaceContext, _workspaces, "acme quarterly revenue");
			return recalled.Facts.Any(f => f.Topic == target);
		}, ct);

		var fact = recalled!.Facts.First(f => f.Topic == target);
		await Assert.That(fact.Fact).IsEqualTo("The figure landed at 4.2 million.");
	}
}

file sealed class FixedWorkspaceContext : WorkspaceContext {
	public FixedWorkspaceContext() : base(httpContextAccessor: null) { }
	public override string Current => WorkspaceNaming.DefaultName;
}

file sealed class AllowAllAuthorizationProvider : Plugin, IAuthorizationProvider {
	public ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) =>
		ValueTask.FromResult(true);
}

file sealed class TestHttpContextAccessor : IHttpContextAccessor {
	public HttpContext? HttpContext { get; set; } = new DefaultHttpContext {
		User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test"))
	};
}
