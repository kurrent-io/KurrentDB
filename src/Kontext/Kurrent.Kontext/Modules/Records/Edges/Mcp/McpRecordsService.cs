// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using static Kurrent.Kontext.Records.Mcp.McpRecordMappers;
using QueryResult = Kurrent.Kontext.Records.Mcp.Model.QueryResult;
using SearchOptions = Kurrent.Kontext.Records.Mcp.Model.SearchOptions;
using SearchResult = Kurrent.Kontext.Records.Mcp.Model.SearchResult;
using RecordsContracts = Kurrent.Kontext.Contracts.Records;

namespace Kurrent.Kontext.Records.Mcp;

/// <summary>
/// The MCP edge for the records service. Unlike the gRPC edge (a pure pass-through over the canonical
/// model), the MCP tool speaks an HTTP-friendly model (<c>Edges.Mcp.Model</c>) so the generated tool
/// schema stays clean: the contract's two oneofs become four optional members, and
/// <see cref="McpRecordMappers"/> enforces the exclusivity JSON schema cannot state.
/// All agent-facing text (tool, parameter, and schema descriptions) lives in <c>McpInstructions.resx</c>,
/// applied at registration by <c>WithToolsFromResources</c> — condensed from <c>records.proto</c>; when the
/// proto instructions change, re-condense there.
/// </summary>
[McpServerToolType]
public sealed class McpRecordsService(IKontextRecords service, IHttpContextAccessor httpContext) {
    [McpServerTool(
        Name = "search", UseStructuredContent = true, ReadOnly = true, Idempotent = true,
        OpenWorld = false)]
    public async ValueTask<SearchResult> SearchAsync(
        string query,
        SearchOptions? options = null,
        CancellationToken ct = default
    ) {
        options ??= new();

        var response = await service.SearchAsync(ToContract(query, options), ct).ConfigureAwait(false);

        return ToModel(response);
    }

    [McpServerTool(
        Name = "query", UseStructuredContent = true, ReadOnly = true, Idempotent = true,
        OpenWorld = false)]
    public async ValueTask<QueryResult> QueryAsync(
        string sql,
        int limit = 0,
        CancellationToken ct = default
    ) {
        var request = new RecordsContracts.QueryRequest { Sql = sql, Limit = limit };

        var response = await service.QueryAsync(request, Principal, ct).ConfigureAwait(false);

        return ToModel(response);
    }

    /// <summary>
    /// The caller, for the one operation that authorizes. The MCP pipeline rejects an unauthenticated
    /// request before a tool runs, so an absent context here is a wiring fault rather than an anonymous
    /// caller — and it must fail closed, never fall back to a principal that would pass the check.
    /// </summary>
    ClaimsPrincipal Principal =>
        httpContext.HttpContext?.User
     ?? throw new InvalidOperationException("No HTTP context: the caller cannot be identified.");
}
