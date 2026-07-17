// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;

namespace Kurrent.Kontext.Edges.Grpc;

/// <summary>
/// The gRPC edge for the memory service — a thin transport shim over <see cref="IKontextMemory"/>.
/// It owns only the gRPC plumbing (<see cref="ServerCallContext"/>, server-streaming writers); all request
/// shaping, domain mapping, and validation live behind the service and its decorators.
/// </summary>
public sealed class GrpcMemoryService(IKontextMemory service) : Contracts.MemoryService.MemoryServiceBase {
	public override async Task<Contracts.RetainResponse> Retain(Contracts.RetainRequest request, ServerCallContext context) =>
		await service.RetainAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task<Contracts.RetractResponse> Retract(Contracts.RetractRequest request, ServerCallContext context) =>
		await service.RetractAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task<Contracts.RecallResponse> Recall(Contracts.RecallRequest request, ServerCallContext context) =>
		await service.RecallAsync(request, context.CancellationToken).ConfigureAwait(false);

	public override async Task Reclaim(
		Contracts.ReclaimRequest request, IServerStreamWriter<Contracts.StoredMemory> responseStream, ServerCallContext context) {
		await foreach (var stored in service.ReclaimAsync(request, context.CancellationToken).ConfigureAwait(false))
            await responseStream.WriteAsync(stored).ConfigureAwait(false);
    }

	public override async Task Recollect(
		Contracts.RecollectRequest request, IServerStreamWriter<Contracts.StoredMemory> responseStream, ServerCallContext context) {
		await foreach (var stored in service.RecollectAsync(request, context.CancellationToken).ConfigureAwait(false))
            await responseStream.WriteAsync(stored).ConfigureAwait(false);
    }

	public override async Task<Contracts.ReflectResponse> Reflect(Contracts.ReflectRequest request, ServerCallContext context) =>
		await service.ReflectAsync(request, context.CancellationToken).ConfigureAwait(false);
}
