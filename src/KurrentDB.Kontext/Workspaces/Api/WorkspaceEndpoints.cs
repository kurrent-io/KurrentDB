// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using EventStore.Plugins.Authorization;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Workspaces.Api;

/// <summary>
/// HTTP API for workspaces. Reads are served from the in-memory
/// <see cref="WorkspaceRegistry"/>; writes go through the Eventuous command service and
/// are picked up by the projection asynchronously.
/// </summary>
public static class WorkspaceEndpoints {
	public static IEndpointRouteBuilder MapKontextWorkspaces(this IEndpointRouteBuilder endpoints, string basePath) {
		var group = endpoints.MapGroup(basePath);

		group.MapPost("", CreateAsync);
		group.MapDelete("{name}", DeleteAsync);
		group.MapPost("{name}/start", StartAsync);
		group.MapPost("{name}/stop", StopAsync);
		group.MapGet("", List);
		group.MapGet("{name}", Get);

		return endpoints;
	}

	private sealed record CreateWorkspaceBody(
		string Name,
		IReadOnlyList<FilterRule> FilterRules,
		bool FullTextIndexingEnabled = true,
		bool SemanticIndexingEnabled = true,
		bool DisableMemory = false,
		bool DisableImports = false,
		bool DisableInquiries = false,
		bool ReadOnly = false);

	private sealed record WorkspaceView(
		string Name,
		IReadOnlyList<FilterRule> FilterRules,
		bool FullTextIndexingEnabled,
		bool SemanticIndexingEnabled,
		bool DisableMemory,
		bool DisableImports,
		bool DisableInquiries,
		bool ReadOnly,
		WorkspaceLifecycle Status);

	static async Task<IResult?> AuthorizeAsync(
		IAuthorizationProvider authz, HttpContext context, OperationDefinition operation, CancellationToken ct) =>
		await authz.CheckAccessAsync(context.User, new Operation(operation), ct)
			? null
			: Results.StatusCode(StatusCodes.Status403Forbidden);

	static async Task<IResult> CreateAsync(
		CreateWorkspaceBody body,
		HttpContext context,
		[FromServices] IAuthorizationProvider authz,
		[FromServices] WorkspaceCommandService commandService,
		CancellationToken ct) {
		if (await AuthorizeAsync(authz, context, Operations.Kontext.Workspaces.Create, ct) is { } denied)
			return denied;

		if (string.IsNullOrWhiteSpace(body.Name))
			return Results.BadRequest(new { error = "Workspace name must not be empty." });

		try {
			var result = await commandService.Handle(
				new CreateWorkspaceRequest(
					body.Name, body.FilterRules,
					body.FullTextIndexingEnabled, body.SemanticIndexingEnabled,
					body.DisableMemory, body.DisableImports, body.DisableInquiries, body.ReadOnly),
				ct);
			result.ThrowIfError();
			return Results.Created($"/kontext/workspaces/{body.Name}",
				new WorkspaceView(
					body.Name, body.FilterRules,
					body.FullTextIndexingEnabled, body.SemanticIndexingEnabled,
					body.DisableMemory, body.DisableImports, body.DisableInquiries, body.ReadOnly,
					WorkspaceLifecycle.Created));
		} catch (WorkspaceInvalidException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (WorkspaceAlreadyExistsException ex) { return Results.Conflict(new { error = ex.Message }); }
	}

	static async Task<IResult> StartAsync(
		string name,
		HttpContext context,
		[FromServices] IAuthorizationProvider authz,
		[FromServices] WorkspaceCommandService commandService,
		CancellationToken ct) {
		if (await AuthorizeAsync(authz, context, Operations.Kontext.Workspaces.Start, ct) is { } denied)
			return denied;

		try {
			var result = await commandService.Handle(new StartWorkspaceRequest(name), ct);
			result.ThrowIfError();
			return Results.NoContent();
		} catch (WorkspaceNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
	}

	static async Task<IResult> StopAsync(
		string name,
		HttpContext context,
		[FromServices] IAuthorizationProvider authz,
		[FromServices] WorkspaceCommandService commandService,
		CancellationToken ct) {
		if (await AuthorizeAsync(authz, context, Operations.Kontext.Workspaces.Stop, ct) is { } denied)
			return denied;

		try {
			var result = await commandService.Handle(new StopWorkspaceRequest(name), ct);
			result.ThrowIfError();
			return Results.NoContent();
		} catch (WorkspaceNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
	}

	static async Task<IResult> DeleteAsync(
		string name,
		HttpContext context,
		[FromServices] IAuthorizationProvider authz,
		[FromServices] WorkspaceCommandService commandService,
		CancellationToken ct) {
		if (await AuthorizeAsync(authz, context, Operations.Kontext.Workspaces.Delete, ct) is { } denied)
			return denied;

		if (WorkspaceNaming.IsDefault(name))
			return Results.BadRequest(new { error = "The 'default' workspace cannot be deleted." });

		try {
			var result = await commandService.Handle(new DeleteWorkspaceRequest(name), ct);
			result.ThrowIfError();
			return Results.NoContent();
		} catch (WorkspaceNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); } catch (WorkspaceInvalidStateException ex) { return Results.Conflict(new { error = ex.Message }); }
	}

	static async Task<IResult> List(
		HttpContext context,
		[FromServices] IAuthorizationProvider authz,
		[FromServices] WorkspaceRegistry registry,
		CancellationToken ct) {
		if (await AuthorizeAsync(authz, context, Operations.Kontext.Workspaces.Read, ct) is { } denied)
			return denied;

		var views = registry.All
			.OrderBy(ws => ws.Name, StringComparer.Ordinal)
			.Select(ws => new WorkspaceView(
				ws.Name, ws.FilterRules,
				ws.FullTextIndexingEnabled, ws.SemanticIndexingEnabled,
				ws.DisableMemory, ws.DisableImports, ws.DisableInquiries, ws.ReadOnly,
				ws.Status))
			.ToList();
		return Results.Ok(views);
	}

	static async Task<IResult> Get(
		string name,
		HttpContext context,
		[FromServices] IAuthorizationProvider authz,
		[FromServices] WorkspaceRegistry registry,
		CancellationToken ct) {
		if (await AuthorizeAsync(authz, context, Operations.Kontext.Workspaces.Read, ct) is { } denied)
			return denied;

		return registry.TryGet(name, out var ws)
			? Results.Ok(new WorkspaceView(
				ws.Name, ws.FilterRules,
				ws.FullTextIndexingEnabled, ws.SemanticIndexingEnabled,
				ws.DisableMemory, ws.DisableImports, ws.DisableInquiries, ws.ReadOnly,
				ws.Status))
			: Results.NotFound(new { error = $"Workspace '{name}' not found." });
	}
}
