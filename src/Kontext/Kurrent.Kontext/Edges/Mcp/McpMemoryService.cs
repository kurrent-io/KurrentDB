// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using ModelContextProtocol.Server;
using static Kurrent.Kontext.Mcp.McpMappers;
using Memory = Kurrent.Kontext.Mcp.Model.Memory;
using RecallOptions = Kurrent.Kontext.Mcp.Model.RecallOptions;
using RecallResult = Kurrent.Kontext.Mcp.Model.RecallResult;
using RecollectOptions = Kurrent.Kontext.Mcp.Model.RecollectOptions;
using ReflectOptions = Kurrent.Kontext.Mcp.Model.ReflectOptions;
using ReflectResult = Kurrent.Kontext.Mcp.Model.ReflectResult;
using RetainResult = Kurrent.Kontext.Mcp.Model.RetainResult;
using RetractResult = Kurrent.Kontext.Mcp.Model.RetractResult;
using StoredMemory = Kurrent.Kontext.Mcp.Model.StoredMemory;

namespace Kurrent.Kontext.Mcp;

/// <summary>
/// The MCP edge for the memory service. Unlike the gRPC edge (a pure pass-through over the canonical model),
/// the MCP tools speak an HTTP-friendly model (<c>Edges.Mcp.Model</c>, ids as plain strings) so the generated
/// tool schemas stay clean. This service maps that model to the gRPC canonical requests (via
/// <see cref="McpMappers"/>), forwards to <see cref="IKontextMemory"/>, and folds the responses back
/// into the model. It is the one place mapping still happens after the core adopted the canonical model.
/// The core's streaming reads (reclaim/recollect) are materialized to lists here: an MCP tool result is a
/// single message, and the tool marshaller cannot serialize <see cref="IAsyncEnumerable{T}"/> (tool
/// creation throws). Streaming consumers use the gRPC edge.
/// All agent-facing text (tool, parameter, and schema descriptions) lives in <c>McpInstructions.resx</c>,
/// applied at registration by <c>WithToolsFromResources</c> — condensed from <c>memory.proto</c>; when the
/// proto instructions change, re-condense there.
/// </summary>
[McpServerToolType]
public sealed class McpMemoryService(IKontextMemory service) {
    [McpServerTool(
        Name = "retain",
        UseStructuredContent = true,
        Destructive = false,
        OpenWorld = false)]
    public async ValueTask<RetainResult> RetainAsync(IReadOnlyList<Memory> memories, bool reconcile = false, CancellationToken ct = default) {
        var request = new Contracts.RetainRequest {
            Reconcile = reconcile,
            Memories = { memories.Select(ToContract) }
        };

        var response = await service
            .RetainAsync(request, ct)
            .ConfigureAwait(false);

        return ToModel(response);
    }

    [McpServerTool(
        Name = "retract", UseStructuredContent = true, Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public async ValueTask<RetractResult> RetractAsync(
        string memoryId,
        string? reason = null,
        CancellationToken ct = default
    ) {
        var request  = new Contracts.RetractRequest {
            MemoryId = memoryId,
            Reason = reason ?? ""
        };
        
        var response = await service.RetractAsync(request, ct).ConfigureAwait(false);
       
        return ToModel(response);
    }

    // Declared read-only even though recall refreshes the recency clock of every memory it returns
    // (reconsolidation): that is the store's internal bookkeeping, not caller-visible state, and agents
    // must be able to recall freely without clients gating it behind a confirmation. See memory.proto.
    [McpServerTool(
        Name = "recall", UseStructuredContent = true, ReadOnly = true,
        OpenWorld = false)]
    public async ValueTask<RecallResult> RecallAsync(
        string query,
        RecallOptions? options = null,
        CancellationToken ct = default
    ) {
        options ??= new();

        var request = new Contracts.RecallRequest {
            Query       = query,
            QueryId     = options.QueryId ?? "",
            Limit       = options.Limit,
            MinScore    = options.MinScore,
            IncludeFull = options.IncludeFull,
            Tags        = { options.Tags.Select(ToContract) }
        };

        var response = await service.RecallAsync(request, ct).ConfigureAwait(false);

        return ToModel(response);
    }

    [McpServerTool(
        Name = "reclaim", UseStructuredContent = true, ReadOnly = true,
        OpenWorld = false)]
    public async ValueTask<IReadOnlyList<StoredMemory>> ReclaimAsync(
        IReadOnlyList<string> ids,
        CancellationToken ct = default
    ) {
        var request = new Contracts.ReclaimRequest { Ids = { ids } };

        var memories = new List<StoredMemory>();

        await foreach (var stored in service.ReclaimAsync(request, ct).ConfigureAwait(false))
            memories.Add(ToModel(stored));

        return memories;
    }

    [McpServerTool(
        Name = "recollect", UseStructuredContent = true, ReadOnly = true,
        OpenWorld = false)]
    public async ValueTask<IReadOnlyList<StoredMemory>> RecollectAsync(
        RecollectOptions options,
        CancellationToken ct = default
    ) {
        var request = new Contracts.RecollectRequest {
            Limit     = options.Limit,
            Sort      = ToContract(options.Sort),
            Direction = ToContract(options.Direction),
            Types_    = { options.Types.Select(ToContract) },
            Tags      = { options.Tags.Select(ToContract) }
        };

        var memories = new List<StoredMemory>();

        await foreach (var stored in service.RecollectAsync(request, ct).ConfigureAwait(false))
            memories.Add(ToModel(stored));

        return memories;
    }

    [McpServerTool(
        Name = "reflect", UseStructuredContent = true, Destructive = true,
        OpenWorld = false)]
    public async ValueTask<ReflectResult> ReflectAsync(
        string query,
        ReflectOptions? options = null,
        CancellationToken ct = default
    ) {
        options ??= new();

        var request = new Contracts.ReflectRequest {
            Query   = query,
            QueryId = options.QueryId ?? "",
            Tags    = { options.Tags.Select(ToContract) }
        };

        var response = await service.ReflectAsync(request, ct).ConfigureAwait(false);
        return ToModel(response);
    }
}