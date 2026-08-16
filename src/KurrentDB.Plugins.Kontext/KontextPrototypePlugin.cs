// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using EventStore.Plugins;
// using EventStore.Plugins.Authorization;
// using KurrentDB.Common.Configuration;
// using KurrentDB.Kontext;
// using KurrentDB.Kontext.Diagnostics;
// using KurrentDB.Kontext.Mcp;
// using KurrentDB.Kontext.Mcp.Workspace;
// using KurrentDB.Kontext.Workspaces.Api;
// using KurrentDB.Core;
// using KurrentDB.Core.Configuration.Sources;
// using KurrentDB.Core.Settings;
// using KurrentDB.Core.TransactionLog.Chunks;
// using Microsoft.AspNetCore.Builder;
// using Microsoft.AspNetCore.Http;
// using Microsoft.AspNetCore.Routing;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.DependencyInjection.Extensions;
// using Microsoft.Extensions.Hosting;
//
// namespace KurrentDB.Plugins.Kontext;
//
// public class KontextPlugin() : SubsystemsPlugin(name: PluginNames.Kontext, requiredEntitlements: ["KONTEXT"]) {
// 	const string McpBasePath = "/kontext/mcp";
// 	const string McpWorkspacePath = "/kontext/mcp/{workspace}";
// 	const string WorkspacesPath = "/kontext/workspaces";
// 	const string KontextDirName = "kontext";
//
// 	IServiceProvider? _services;
//
// 	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
// 		var enabled = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled", false);
// 		return (enabled, "Set KurrentDB__Kontext__Enabled to true to enable the kontext plugin.");
// 	}
//
// 	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
// 		var section = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:Kontext");
// 		var embeddingsConfig = section.GetSection("Embeddings").Get<KontextEmbeddingsConfig>() ?? new KontextEmbeddingsConfig();
// 		var configuredPath = section["Path"];
//
// 		services.TryAddSingleton(embeddingsConfig);
// 		services.TryAddSingleton(sp => new KontextStorageConfig {
// 			DataPath = string.IsNullOrWhiteSpace(configuredPath)
// 				? ResolveKontextDirectory(sp)
// 				: configuredPath,
// 		});
// 		services.AddHttpContextAccessor();
// 		services.TryAddSingleton<GetWriterCheckpoint>(sp =>
// 			sp.GetRequiredService<TFChunkDbConfig>().WriterCheckpoint.Read);
//
// 		services.AddKontext().WithHttpTransport(options => {
// #pragma warning disable MCPEXP0001, MCPEXP002 // RunSessionHandler is experimental
// 			options.RunSessionHandler = async (context, server, ct) => {
// 				var sessions = context.RequestServices.GetRequiredService<ActiveMcpSessions>();
// 				var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
// 				var workspace = context.GetRouteValue("workspace") as string
// 					?? throw new InvalidOperationException("MCP session route is missing the 'workspace' value.");
// 				sessions.Add(server.SessionId!, baseUrl, workspace, context.User);
// 				try {
// 					await server.RunAsync(ct);
// 				} finally {
// 					sessions.Remove(server.SessionId!);
// 				}
// 			};
// #pragma warning restore MCPEXP0001, MCPEXP002
// 		});
// 	}
//
// 	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
// 		_services = app.ApplicationServices;
//
// 		// Require an authenticated user to connect to an MCP workspace
// 		app.Use(async (context, next) => {
// 			if (context.Request.Path.StartsWithSegments(McpBasePath)) {
// 				var authz = context.RequestServices.GetRequiredService<IAuthorizationProvider>();
// 				if (!await authz.CheckAccessAsync(
// 					context.User, new Operation(Operations.Kontext.Workspaces.Connect), context.RequestAborted)) {
// 					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
// 					return;
// 				}
// 			}
// 			await next();
// 		});
//
// 		app.UseEndpoints(endpoints => {
// 			endpoints.MapMcp(McpWorkspacePath);
// 			endpoints.MapKontextBulkImport(ImportTool.ImportRouteTemplate);
// 			endpoints.MapKontextWorkspaces(WorkspacesPath);
// 		});
// 	}
//
// 	public override Task Start() {
// 		// The host calls Start() even on disabled plugins, but ConfigureServices /
// 		// ConfigureApplication are skipped — so _services is null. Nothing to initialize.
// 		if (_services is null)
// 			return Task.CompletedTask;
//
// 		var lifetime = _services.GetService<IHostApplicationLifetime>();
// 		return _services.InitializeKontextAsync(lifetime?.ApplicationStopping ?? default);
// 	}
//
// 	static string ResolveKontextDirectory(IServiceProvider sp) {
// 		var options = sp.GetRequiredService<ClusterVNodeOptions>();
// 		var indexPath = options.Database.Index
// 			?? Path.Combine(options.Database.Db, ESConsts.DefaultIndexDirectoryName);
//
// 		return Path.Combine(indexPath, KontextDirName);
// 	}
// }
//
// public static class KontextEndpointRouteBuilderExtensions {
// 	public static IEndpointConventionBuilder MapKontextBulkImport(
// 		this IEndpointRouteBuilder endpoints, string pattern) {
// 		var sessions = endpoints.ServiceProvider.GetRequiredService<ActiveMcpSessions>();
//
// 		return endpoints.MapPost(pattern, async (HttpContext context, string workspace) => {
// 			var sessionId = (string?)context.Request.Headers["Mcp-Session-Id"];
//
// 			if (string.IsNullOrEmpty(sessionId) || !sessions.Contains(sessionId)) {
// 				context.Response.StatusCode = 401;
// 				await context.Response.WriteAsync("Missing or invalid Mcp-Session-Id header.");
// 				return;
// 			}
//
// 			var registry = context.RequestServices.GetRequiredService<WorkspaceRegistry>();
// 			if (!registry.TryGet(workspace, out var entry)) {
// 				context.Response.StatusCode = 404;
// 				await context.Response.WriteAsync($"Workspace '{workspace}' not found.");
// 				return;
// 			}
//
// 			if (!sessions.IsBoundTo(sessionId, workspace)) {
// 				context.Response.StatusCode = 403;
// 				await context.Response.WriteAsync($"This session is not bound to workspace '{workspace}'.");
// 				return;
// 			}
//
// 			try { entry.EnsureImportable(); } catch (WorkspaceOperationDisabledException ex) {
// 				context.Response.StatusCode = 403;
// 				await context.Response.WriteAsync(ex.Message);
// 				return;
// 			}
//
// 			ImportEvent[]? events;
// 			try {
// 				events = await JsonSerializer.DeserializeAsync<ImportEvent[]>(
// 					context.Request.Body, JsonOptions.Compact, context.RequestAborted);
// 			} catch (JsonException) {
// 				context.Response.StatusCode = 400;
// 				await context.Response.WriteAsync("Invalid JSON payload.");
// 				return;
// 			}
//
// 			var authz = context.RequestServices.GetRequiredService<IAuthorizationProvider>();
// 			var principal = sessions.GetPrincipal(sessionId)!; // session presence checked above
//
// 			var (valid, errors) = await ImportValidator.Validate(
// 				events ?? [],
// 				entry.FilterRules.Select(r => r.StreamPrefix).ToArray(),
// 				stream => authz.CanWriteStreamAsync(principal, stream, context.RequestAborted));
//
// 			if (valid.Count > 0) {
// 				var client = context.RequestServices.GetRequiredService<ISystemClient>();
// 				await client.WriteBatchAsync(valid);
// 			}
//
// 			context.Response.ContentType = "application/json";
// 			await context.Response.WriteAsync(ImportValidator.FormatResult(valid, errors));
// 		});
// 	}
// }