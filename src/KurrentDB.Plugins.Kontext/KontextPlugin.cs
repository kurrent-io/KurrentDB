// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Configuration;
using KurrentDB.Kontext;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Mcp;
using KurrentDB.Kontext.Mcp.Workspace;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Settings;
using KurrentDB.Core.TransactionLog.Chunks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Plugins.Kontext;

public class KontextPlugin() : SubsystemsPlugin(name: PluginNames.Kontext, requiredEntitlements: ["KONTEXT"]) {
	const string McpBasePath = "/kontext/mcp";
	const string McpWorkspacePath = "/kontext/mcp/{workspace}";
	const string WorkspacesPath = "/kontext/workspaces";
	const string KontextDirName = "kontext";

	IServiceProvider? _services;

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled", false);
		return (enabled, "Set KurrentDB__Kontext__Enabled to true to enable the kontext plugin.");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var section = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:Kontext");
		var embeddingsConfig = section.GetSection("Embeddings").Get<KontextEmbeddingsConfig>() ?? new KontextEmbeddingsConfig();
		var configuredPath = section["Path"];

		services.TryAddSingleton(embeddingsConfig);
		services.TryAddSingleton(sp => new KontextStorageConfig {
			DataPath = string.IsNullOrWhiteSpace(configuredPath)
				? ResolveKontextDirectory(sp)
				: configuredPath,
		});
		services.AddHttpContextAccessor();
		services.TryAddSingleton<GetWriterCheckpoint>(sp =>
			sp.GetRequiredService<TFChunkDbConfig>().WriterCheckpoint.Read);

		services.AddKontext().WithHttpTransport(options => {
#pragma warning disable MCPEXP0001, MCPEXP002 // RunSessionHandler is experimental
			options.RunSessionHandler = async (context, server, ct) => {
				var sessions = context.RequestServices.GetRequiredService<ActiveMcpSessions>();
				var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
				var workspace = context.GetRouteValue("workspace") as string
					?? throw new InvalidOperationException("MCP session route is missing the 'workspace' value.");
				sessions.Add(server.SessionId!, baseUrl, workspace, context.User);
				try {
					await server.RunAsync(ct);
				} finally {
					sessions.Remove(server.SessionId!);
				}
			};
#pragma warning restore MCPEXP0001, MCPEXP002
		});
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		_services = app.ApplicationServices;

		// Require an authenticated user to connect to an MCP workspace
		app.Use(async (context, next) => {
			if (context.Request.Path.StartsWithSegments(McpBasePath)) {
				var authz = context.RequestServices.GetRequiredService<IAuthorizationProvider>();
				if (!await authz.CheckAccessAsync(
					context.User, new Operation(Operations.Kontext.Workspaces.Connect), context.RequestAborted)) {
					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
					return;
				}
			}
			await next();
		});

		app.UseEndpoints(endpoints => {
			endpoints.MapMcp(McpWorkspacePath);
			endpoints.MapKontextBulkImport(ImportTool.ImportRouteTemplate);
			endpoints.MapKontextWorkspaces(WorkspacesPath);
		});
	}

	public override Task Start() {
		// The host calls Start() even on disabled plugins, but ConfigureServices /
		// ConfigureApplication are skipped — so _services is null. Nothing to initialize.
		if (_services is null)
			return Task.CompletedTask;

		var lifetime = _services.GetService<IHostApplicationLifetime>();
		return _services.InitializeKontextAsync(lifetime?.ApplicationStopping ?? default);
	}

	static string ResolveKontextDirectory(IServiceProvider sp) {
		var options = sp.GetRequiredService<ClusterVNodeOptions>();
		var indexPath = options.Database.Index
			?? Path.Combine(options.Database.Db, ESConsts.DefaultIndexDirectoryName);

		return Path.Combine(indexPath, KontextDirName);
	}
}
