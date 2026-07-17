// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext;

/// <summary>
/// The memory service is the edge that allows agents to store, retrieve, and manage memories in the Kontext system.
/// The canonical protobuf contracts are used for the wire, but the service itself is agnostic to transport and serialization.
/// The gRPC edge is a thin shim over this service.
/// </summary>
public interface IKontextMemory {
	ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default);

	ValueTask<Contracts.RetractResponse> RetractAsync(Contracts.RetractRequest request, CancellationToken ct = default);

	ValueTask<Contracts.RecallResponse> RecallAsync(Contracts.RecallRequest request, CancellationToken ct = default);

	IAsyncEnumerable<Contracts.StoredMemory> ReclaimAsync(Contracts.ReclaimRequest request, CancellationToken ct = default);

	IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, CancellationToken ct = default);

	ValueTask<Contracts.ReflectResponse> ReflectAsync(Contracts.ReflectRequest request, CancellationToken ct = default);
}