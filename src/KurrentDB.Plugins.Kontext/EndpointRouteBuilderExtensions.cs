// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using EventStore.Plugins.Authorization;
using KurrentDB.Kontext;
using KurrentDB.Kontext.Mcp;
using KurrentDB.Kontext.Mcp.Workspace;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

public static class KontextEndpointRouteBuilderExtensions {
	public static IEndpointConventionBuilder MapKontextBulkImport(
		this IEndpointRouteBuilder endpoints, string pattern) {
		var sessions = endpoints.ServiceProvider.GetRequiredService<ActiveMcpSessions>();

		return endpoints.MapPost(pattern, async (HttpContext context, string workspace) => {
			var sessionId = (string?)context.Request.Headers["Mcp-Session-Id"];

			if (string.IsNullOrEmpty(sessionId) || !sessions.Contains(sessionId)) {
				context.Response.StatusCode = 401;
				await context.Response.WriteAsync("Missing or invalid Mcp-Session-Id header.");
				return;
			}

			var registry = context.RequestServices.GetRequiredService<WorkspaceRegistry>();
			if (!registry.TryGet(workspace, out var entry)) {
				context.Response.StatusCode = 404;
				await context.Response.WriteAsync($"Workspace '{workspace}' not found.");
				return;
			}

			if (!sessions.IsBoundTo(sessionId, workspace)) {
				context.Response.StatusCode = 403;
				await context.Response.WriteAsync($"This session is not bound to workspace '{workspace}'.");
				return;
			}

			try { entry.EnsureImportable(); } catch (WorkspaceOperationDisabledException ex) {
				context.Response.StatusCode = 403;
				await context.Response.WriteAsync(ex.Message);
				return;
			}

			ImportEvent[]? events;
			try {
				events = await JsonSerializer.DeserializeAsync<ImportEvent[]>(
					context.Request.Body, JsonOptions.Compact, context.RequestAborted);
			} catch (JsonException) {
				context.Response.StatusCode = 400;
				await context.Response.WriteAsync("Invalid JSON payload.");
				return;
			}

			var authz = context.RequestServices.GetRequiredService<IAuthorizationProvider>();
			var principal = sessions.GetPrincipal(sessionId)!; // session presence checked above

			var (valid, errors) = await ImportValidator.Validate(
				events ?? [],
				entry.FilterRules.Select(r => r.StreamPrefix).ToArray(),
				stream => authz.CanWriteStreamAsync(principal, stream, context.RequestAborted));

			if (valid.Count > 0) {
				var client = context.RequestServices.GetRequiredService<ISystemClient>();
				await client.WriteBatchAsync(valid);
			}

			context.Response.ContentType = "application/json";
			await context.Response.WriteAsync(ImportValidator.FormatResult(valid, errors));
		});
	}
}
