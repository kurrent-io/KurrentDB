// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EventStore.Plugins;
using EventStore.Plugins.Authorization;
using KurrentDB.Kontext.Mcp;
using KurrentDB.Kontext.Mcp.Workspace;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Plugins.Kontext.Tests;

public class BulkImportEndpointTests {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	ISystemClient SystemClient => NodeShim.Node.Services.GetRequiredService<ISystemClient>();

	const string SessionId = "test-session-1";
	const string Workspace = "default";

	// Allows every stream by default; streams passed to Deny are refused for writes.
	sealed class FakeAuthorizationProvider : Plugin, IAuthorizationProvider {
		readonly HashSet<string> _denied = [];

		public FakeAuthorizationProvider Deny(string stream) {
			_denied.Add(stream);
			return this;
		}

		public ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) {
			var streamId = operation.Parameters.ToArray()
				.FirstOrDefault(p => p.Name == "streamId").Value;
			return ValueTask.FromResult(streamId == null || !_denied.Contains(streamId));
		}
	}

	static readonly ClaimsPrincipal TestUser =
		new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test"));

	async Task<(IHost Host, HttpClient Client, WorkspaceRegistry Registry, ActiveMcpSessions Sessions)>
		CreateTestServer(bool disableImports = false, bool readOnly = false, FakeAuthorizationProvider? authz = null) {
		var sessions = new ActiveMcpSessions();
		sessions.Add(SessionId, "http://localhost", Workspace, TestUser);

		var registry = new WorkspaceRegistry();
		registry.Upsert(WorkspaceEntry.Create(
			name: Workspace,
			filterRules: [new FilterRule("", null)],
			fullTextIndexingEnabled: false,
			semanticIndexingEnabled: false,
			disableMemory: false,
			disableImports: disableImports,
			disableInquiries: false,
			readOnly: readOnly));

		var host = new HostBuilder()
			.ConfigureWebHost(webBuilder => {
				webBuilder.UseTestServer();
				webBuilder.ConfigureServices(services => {
					services.AddSingleton(SystemClient);
					services.AddSingleton(sessions);
					services.AddSingleton(registry);
					services.AddSingleton<IAuthorizationProvider>(authz ?? new FakeAuthorizationProvider());
					services.AddRouting();
				});
				webBuilder.Configure(app => {
					app.UseRouting();
					app.UseEndpoints(endpoints =>
						endpoints.MapKontextBulkImport(ImportTool.ImportRouteTemplate));
				});
			})
			.Build();

		await host.StartAsync();
		return (host, host.GetTestClient(), registry, sessions);
	}

	static StringContent JsonBody(object body) =>
		new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

	static HttpRequestMessage ImportRequest(string workspace, HttpContent body, string? sessionId = SessionId) {
		var req = new HttpRequestMessage(HttpMethod.Post, $"/kontext/{workspace}/import") { Content = body };
		if (sessionId != null)
			req.Headers.Add("Mcp-Session-Id", sessionId);
		return req;
	}

	[Test]
	public async Task Returns_401_When_Mcp_Session_Header_Missing(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var response = await client.SendAsync(ImportRequest(Workspace, JsonBody(Array.Empty<object>()), sessionId: null), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Returns_401_When_Session_Unknown(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var response = await client.SendAsync(ImportRequest(Workspace, JsonBody(Array.Empty<object>()), sessionId: "ghost"), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Returns_403_When_Session_Bound_To_Another_Workspace(CancellationToken ct) {
		// The session is bound to 'default'; importing into a different (existing) workspace by
		// name must be refused even though the session is otherwise valid.
		var (host, client, registry, _) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		registry.Upsert(WorkspaceEntry.Create(
			name: "other-workspace",
			filterRules: [new FilterRule("", null)],
			fullTextIndexingEnabled: false,
			semanticIndexingEnabled: false,
			disableMemory: false,
			disableImports: false,
			disableInquiries: false,
			readOnly: false));

		var response = await client.SendAsync(ImportRequest("other-workspace", JsonBody(Array.Empty<object>())), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Returns_404_When_Workspace_Not_Found(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var response = await client.SendAsync(ImportRequest("ghost", JsonBody(Array.Empty<object>())), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Returns_403_When_Imports_Disabled(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer(disableImports: true);
		using var _h = host;
		using var _c = client;

		var response = await client.SendAsync(ImportRequest(Workspace, JsonBody(Array.Empty<object>())), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Returns_403_When_Workspace_ReadOnly(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer(readOnly: true);
		using var _h = host;
		using var _c = client;

		var response = await client.SendAsync(ImportRequest(Workspace, JsonBody(Array.Empty<object>())), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Returns_400_When_Body_Is_Invalid_Json(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var body = new StringContent("not json", Encoding.UTF8, "application/json");
		var response = await client.SendAsync(ImportRequest(Workspace, body), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Successful_Import_Writes_Events_And_Returns_Result(CancellationToken ct) {
		var (host, client, _, _) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var streamA = $"bulk-import-a-{Guid.NewGuid():N}";
		var streamB = $"bulk-import-b-{Guid.NewGuid():N}";
		var body = JsonBody(new object[] {
			new { stream = streamA, eventType = "OrderPlaced", data = new { product = "Widget" } },
			new { stream = streamA, eventType = "OrderShipped", data = new { carrier = "FedEx" } },
			new { stream = streamB, eventType = "Created", data = new { email = "a@b.com" } },
		});

		var response = await client.SendAsync(ImportRequest(Workspace, body), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		var responseBody = await response.Content.ReadAsStringAsync(ct);
		using var doc = JsonDocument.Parse(responseBody);
		doc.RootElement.GetProperty("imported").GetInt32().ShouldBe(3);
	}

	[Test]
	public async Task Skips_Events_For_Streams_The_User_Cannot_Write(CancellationToken ct) {
		var deniedStream = $"bulk-import-denied-{Guid.NewGuid():N}";
		var allowedStream = $"bulk-import-allowed-{Guid.NewGuid():N}";

		var (host, client, _, _) = await CreateTestServer(
			authz: new FakeAuthorizationProvider().Deny(deniedStream));
		using var _h = host;
		using var _c = client;

		var body = JsonBody(new object[] {
			new { stream = allowedStream, eventType = "OrderPlaced", data = new { product = "Widget" } },
			new { stream = deniedStream, eventType = "Created", data = new { email = "a@b.com" } },
		});

		var response = await client.SendAsync(ImportRequest(Workspace, body), ct);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		var responseBody = await response.Content.ReadAsStringAsync(ct);
		using var doc = JsonDocument.Parse(responseBody);
		// Only the allowed stream's event is written; the denied one is reported as an error
		// against its original index.
		doc.RootElement.GetProperty("imported").GetInt32().ShouldBe(1);
		var errors = doc.RootElement.GetProperty("errors");
		errors.GetArrayLength().ShouldBe(1);
		errors[0].GetProperty("index").GetInt32().ShouldBe(1);
		errors[0].GetProperty("message").GetString()!.ShouldContain("write access denied");
	}
}