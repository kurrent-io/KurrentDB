// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Validation;

namespace Kurrent.Kontext.Infrastructure.FluentValidation;

/// <summary>
/// Validates every request against its registered validator before delegating to the inner
/// <see cref="IKontextMemory"/>. On failure it throws an edge-neutral
/// <see cref="RequestValidationException"/>; each edge maps that to its own error (gRPC → InvalidArgument,
/// HTTP → 400). Validation runs eagerly, including for the streaming operations, so an invalid request
/// fails before any records are produced rather than partway through the stream.
/// </summary>
public sealed class KontextMemoryValidationDecorator(IKontextMemory inner, RequestValidationService validation) : IKontextMemory {
    public ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) {
        validation.Validate(request);
        return inner.RetainAsync(request, ct);
    }

    public ValueTask<Contracts.RetractResponse> RetractAsync(Contracts.RetractRequest request, CancellationToken ct = default) {
        validation.Validate(request);
        return inner.RetractAsync(request, ct);
    }

    public ValueTask<Contracts.RecallResponse> RecallAsync(Contracts.RecallRequest request, CancellationToken ct = default) {
        validation.Validate(request);
        return inner.RecallAsync(request, ct);
    }

    public IAsyncEnumerable<Contracts.StoredMemory> ReclaimAsync(Contracts.ReclaimRequest request, CancellationToken ct = default) {
        validation.Validate(request);
        return inner.ReclaimAsync(request, ct);
    }

    public IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, CancellationToken ct = default) {
        validation.Validate(request);
        return inner.RecollectAsync(request, ct);
    }

    public ValueTask<Contracts.ReflectResponse> ReflectAsync(Contracts.ReflectRequest request, CancellationToken ct = default) {
        validation.Validate(request);
        return inner.ReflectAsync(request, ct);
    }
}