// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Contracts = Kurrent.Kontext.Contracts;
using Core = Kurrent.Kontext.Core;
using static KurrentDB.Kontext.Reloaded.Edges.Grpc.MemoryMappers;

namespace KurrentDB.Kontext.Reloaded.Edges.Grpc;

/// <summary>
/// The gRPC edge for the memory service — a thin adapter that maps the v3 contract messages to and from the
/// domain and delegates to <see cref="Core.IKontextMemory"/>. All wire mapping lives in <c>MemoryMappers</c>.
/// </summary>
public sealed class KontextMemoryService(Core.IKontextMemory memory) : Contracts.MemoryService.MemoryServiceBase {
	public override async Task<Contracts.RetainResponse> Retain(Contracts.RetainRequest request, ServerCallContext context) {
		var memories = request.Memories.Select(ToDomain).ToList();
		var result = await memory.RetainAsync(memories, request.Reconcile, context.CancellationToken).ConfigureAwait(false);
		return ToContract(result);
	}

	public override async Task<Contracts.RetractResponse> Retract(Contracts.RetractRequest request, ServerCallContext context) {
		var result = await memory
			.RetractAsync(Core.MemoryId.Parse(request.MemoryId), request.Reason, context.CancellationToken)
			.ConfigureAwait(false);
		return ToContract(result);
	}

	public override async Task<Contracts.RecallResponse> Recall(Contracts.RecallRequest request, ServerCallContext context) {
		var options = new Core.RecallOptions {
			QueryId = string.IsNullOrEmpty(request.QueryId) ? null : Core.QueryId.Parse(request.QueryId),
			Limit = request.Limit,
			MinScore = request.MinScore,
			Tags = request.Tags.Select(ToDomain).ToList(),
			IncludeFull = request.IncludeFull,
		};
		
        var result = await memory.RecallAsync(request.Query, options, context.CancellationToken).ConfigureAwait(false);
		
        return ToContract(result);
	}

	public override async Task Reclaim(
		Contracts.ReclaimRequest request, IServerStreamWriter<Contracts.StoredMemory> responseStream, ServerCallContext context) {
		var ids = request.Ids.Select(id => Core.MemoryId.Parse(id)).ToList();
		await foreach (var stored in memory.ReclaimAsync(ids, context.CancellationToken).ConfigureAwait(false)) {
			await responseStream.WriteAsync(ToContract(stored)).ConfigureAwait(false);
		}
	}

	public override async Task Recollect(
		Contracts.RecollectRequest request, IServerStreamWriter<Contracts.StoredMemory> responseStream, ServerCallContext context) {
		var options = new Core.RecollectOptions {
			// `Types_` (trailing underscore): protobuf renames the `types` field to avoid clashing with its
			// nested `Types` container convention.
			Types = request.Types_.Select(ToDomain).ToList(),
			Tags = request.Tags.Select(ToDomain).ToList(),
			IncludeRetracted = request.IncludeRetracted,
			Limit = request.Limit,
			Sort = ToDomain(request.Sort),
			Direction = ToDomain(request.Direction),
		};
		await foreach (var stored in memory.RecollectAsync(options, context.CancellationToken).ConfigureAwait(false)) {
			await responseStream.WriteAsync(ToContract(stored)).ConfigureAwait(false);
		}
	}

	public override async Task<Contracts.ReflectResponse> Reflect(Contracts.ReflectRequest request, ServerCallContext context) {
		var options = new Core.ReflectOptions {
			QueryId = string.IsNullOrEmpty(request.QueryId) ? null : Core.QueryId.Parse(request.QueryId),
			Tags = request.Tags.Select(ToDomain).ToList(),
		};
		var result = await memory.ReflectAsync(request.Query, options, context.CancellationToken).ConfigureAwait(false);
		return ToContract(result);
	}
}
