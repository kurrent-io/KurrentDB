// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Edges.Grpc;

/// <summary>
/// The gRPC edge for the memory service — a thin transport shim over <see cref="IKontextMemory"/>.
/// It owns only the gRPC plumbing (<see cref="ServerCallContext"/>, server-streaming writers); all request
/// shaping, domain mapping, and validation live behind the service and its decorators.
/// </summary>
public sealed class GrpcMemoryService(IKontextMemory service) : MemoryContracts.MemoryService.MemoryServiceBase {
	public override async Task<MemoryContracts.RetainResponse> Retain(MemoryContracts.RetainRequest request, ServerCallContext context) =>
		await service.RetainAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task<MemoryContracts.RetractResponse> Retract(MemoryContracts.RetractRequest request, ServerCallContext context) =>
		await service.RetractAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task<MemoryContracts.RecallResponse> Recall(MemoryContracts.RecallRequest request, ServerCallContext context) =>
		await service.RecallAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task Reclaim(
		MemoryContracts.ReclaimRequest request, IServerStreamWriter<MemoryContracts.StoredMemory> responseStream, ServerCallContext context) {
		await foreach (var stored in service.ReclaimAsync(request, context.CancellationToken).ConfigureAwait(false))
            await responseStream.WriteAsync(stored).ConfigureAwait(false);
    }

	public override async Task Recollect(
		MemoryContracts.RecollectRequest request, IServerStreamWriter<MemoryContracts.StoredMemory> responseStream, ServerCallContext context) {
		await foreach (var stored in service.RecollectAsync(request, context.CancellationToken).ConfigureAwait(false))
            await responseStream.WriteAsync(stored).ConfigureAwait(false);
    }

	public override async Task<MemoryContracts.ReflectResponse> Reflect(MemoryContracts.ReflectRequest request, ServerCallContext context) =>
		await service.ReflectAsync(request, context.CancellationToken).ConfigureAwait(false);
}
