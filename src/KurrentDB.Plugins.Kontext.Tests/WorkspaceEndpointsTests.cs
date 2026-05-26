// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EventStore.Plugins;
using EventStore.Plugins.Authorization;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Plugins.Kontext.Tests;

public class WorkspaceEndpointsTests {
	const string BasePath = "/kontext/workspaces";

	static WorkspaceEntry Entry(string name, bool readOnly = false) =>
		WorkspaceEntry.Create(
			name, [new FilterRule("", null)],
			fullTextIndexingEnabled: true, semanticIndexingEnabled: true,
			disableMemory: false, disableImports: false, disableInquiries: false,
			readOnly: readOnly);

	sealed class AllowAllAuthorizationProvider : Plugin, IAuthorizationProvider {
		public ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) => new(true);
	}

	static async Task<(IHost Host, HttpClient Client)> CreateTestServer(params WorkspaceEntry[] entries) {
		var registry = new WorkspaceRegistry();
		foreach (var entry in entries)
			registry.Upsert(entry);

		var host = new HostBuilder()
			.ConfigureWebHost(webBuilder => {
				webBuilder.UseTestServer();
				webBuilder.ConfigureServices(services => {
					services.AddSingleton(registry);
					services.AddSingleton<IAuthorizationProvider>(new AllowAllAuthorizationProvider());
					// The write endpoints bind the command service via [FromServices]; register one
					// whose store deps are never touched (these tests stop at the guard checks).
					services.AddSingleton(new WorkspaceCommandService(
						new WorkspaceEventStore(inner: null!, client: null!, Kurrent.Surge.Schema.SchemaRegistry.Global),
						new WorkspaceStreamNameMap()));
					services.AddRouting();
				});
				webBuilder.Configure(app => {
					app.UseRouting();
					app.UseEndpoints(endpoints => endpoints.MapKontextWorkspaces(BasePath));
				});
			})
			.Build();

		await host.StartAsync();
		return (host, host.GetTestClient());
	}

	[Test]
	public async Task List_Returns_Empty_Array_When_No_Workspaces(CancellationToken ct) {
		var (host, client) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var response = await client.GetAsync(BasePath, ct);
		response.StatusCode.ShouldBe(HttpStatusCode.OK);

		var body = await response.Content.ReadAsStringAsync(ct);
		using var doc = JsonDocument.Parse(body);
		doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
		doc.RootElement.GetArrayLength().ShouldBe(0);
	}

	[Test]
	public async Task List_Returns_All_Workspaces_Ordered_By_Name(CancellationToken ct) {
		var (host, client) = await CreateTestServer(Entry("gamma"), Entry("alpha"), Entry("beta"));
		using var _h = host;
		using var _c = client;

		var response = await client.GetAsync(BasePath, ct);
		var body = await response.Content.ReadAsStringAsync(ct);
		using var doc = JsonDocument.Parse(body);

		doc.RootElement.GetArrayLength().ShouldBe(3);
		doc.RootElement[0].GetProperty("name").GetString().ShouldBe("alpha");
		doc.RootElement[1].GetProperty("name").GetString().ShouldBe("beta");
		doc.RootElement[2].GetProperty("name").GetString().ShouldBe("gamma");
	}

	[Test]
	public async Task Get_Returns_Workspace_When_Exists(CancellationToken ct) {
		var (host, client) = await CreateTestServer(Entry("alpha"));
		using var _h = host;
		using var _c = client;

		var response = await client.GetAsync($"{BasePath}/alpha", ct);
		response.StatusCode.ShouldBe(HttpStatusCode.OK);

		var body = await response.Content.ReadAsStringAsync(ct);
		using var doc = JsonDocument.Parse(body);
		doc.RootElement.GetProperty("name").GetString().ShouldBe("alpha");
	}

	[Test]
	public async Task Get_Returns_404_When_Workspace_Missing(CancellationToken ct) {
		var (host, client) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var response = await client.GetAsync($"{BasePath}/ghost", ct);
		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Get_Surfaces_Workspace_Flags(CancellationToken ct) {
		var (host, client) = await CreateTestServer(Entry("alpha", readOnly: true));
		using var _h = host;
		using var _c = client;

		var response = await client.GetAsync($"{BasePath}/alpha", ct);
		var body = await response.Content.ReadAsStringAsync(ct);
		using var doc = JsonDocument.Parse(body);

		doc.RootElement.GetProperty("readOnly").GetBoolean().ShouldBeTrue();
		doc.RootElement.GetProperty("disableMemory").GetBoolean().ShouldBeFalse();
	}

	[Test]
	public async Task Delete_Default_Workspace_Returns_BadRequest(CancellationToken ct) {
		var (host, client) = await CreateTestServer();
		using var _h = host;
		using var _c = client;

		var response = await client.DeleteAsync($"{BasePath}/{WorkspaceNaming.DefaultName}", ct);
		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

		var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
		body.GetProperty("error").GetString()!.ShouldContain("cannot be deleted");
	}
}
