// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Memory;

/// <summary>
/// The memory service is the edge that allows agents to store, retrieve, and manage memories in the Kontext system.
/// The canonical protobuf contracts are used for the wire, but the service itself is agnostic to transport and serialization.
/// The gRPC edge is a thin shim over this service.
/// </summary>
public interface IKontextMemory {
	ValueTask<MemoryContracts.RetainResponse> RetainAsync(MemoryContracts.RetainRequest request, CancellationToken ct = default);

	ValueTask<MemoryContracts.RecallResponse> RecallAsync(MemoryContracts.RecallRequest request, CancellationToken ct = default);

	IAsyncEnumerable<MemoryContracts.StoredMemory> ReclaimAsync(MemoryContracts.ReclaimRequest request, CancellationToken ct = default);

	IAsyncEnumerable<MemoryContracts.StoredMemory> RecollectAsync(MemoryContracts.RecollectRequest request, CancellationToken ct = default);

	ValueTask<MemoryContracts.ReflectResponse> ReflectAsync(MemoryContracts.ReflectRequest request, CancellationToken ct = default);
}
