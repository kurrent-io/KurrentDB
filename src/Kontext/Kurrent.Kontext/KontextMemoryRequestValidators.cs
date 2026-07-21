// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// `global::` forces the library namespace over this folder's colliding `FluentValidation` namespace.

using FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;

namespace Kurrent.Kontext.Infrastructure.FluentValidation;

public sealed class RetainRequestValidator : RequestValidator<Contracts.RetainRequest> {
    public RetainRequestValidator() {
        RuleFor(x => x.Memories)
            .NotEmpty()
            .WithMessage("At least one memory is required.");

        RuleForEach(x => x.Memories)
            .Must(m => !string.IsNullOrWhiteSpace(m.Content))
            .WithMessage("Memory content must not be empty.");

        // memory_id is optional (empty = server-assigned) but, when set, must be a well-formed id so the
        // mapper's MemoryId.Parse can't blow up on a bad string.
        RuleForEach(x => x.Memories)
            .Must(m => MemoryRequestRules.EmptyOrGuid(m.MemoryId))
            .WithMessage("memory_id must be empty or a valid UUID.");
    }
}

public sealed class RetractRequestValidator : RequestValidator<Contracts.RetractRequest> {
    public RetractRequestValidator() {
        RuleFor(x => x.MemoryId)
            .NotEmpty()
            .WithMessage("memory_id is required.")
            .Must(MemoryRequestRules.Guid)
            .WithMessage("memory_id must be a valid UUID.");
    }
}

public sealed class RecallRequestValidator : RequestValidator<Contracts.RecallRequest> {
    public RecallRequestValidator() {
        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("query is required.");

        RuleFor(x => x.Limit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("limit must not be negative.");

        RuleFor(x => x.MinScore)
            .GreaterThanOrEqualTo(0)
            .WithMessage("min_score must not be negative.");

        RuleFor(x => x.QueryId)
            .Must(MemoryRequestRules.EmptyOrGuid)
            .WithMessage("query_id must be empty or a valid UUID.");
    }
}

public sealed class ReclaimRequestValidator : RequestValidator<Contracts.ReclaimRequest> {
    public ReclaimRequestValidator() {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithMessage("At least one id is required.");

        RuleForEach(x => x.Ids)
            .Must(MemoryRequestRules.Guid)
            .WithMessage("ids must be valid UUIDs.");
    }
}

public sealed class RecollectRequestValidator : RequestValidator<Contracts.RecollectRequest> {
    public RecollectRequestValidator() {
        RuleFor(x => x.Limit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("limit must not be negative.");
    }
}

public sealed class ReflectRequestValidator : RequestValidator<Contracts.ReflectRequest> {
    public ReflectRequestValidator() {
        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("query is required.");

        RuleFor(x => x.QueryId)
            .Must(MemoryRequestRules.EmptyOrGuid)
            .WithMessage("query_id must be empty or a valid UUID.");
    }
}

/// <summary>Shared id predicates — the request ids are strings on the wire but must round-trip to the
/// Guid-backed domain value objects (<c>MemoryId</c>/<c>QueryId</c>).</summary>
static class MemoryRequestRules {
    public static bool Guid(string value) => System.Guid.TryParse(value, out _);

    public static bool EmptyOrGuid(string value) => string.IsNullOrEmpty(value) || System.Guid.TryParse(value, out _);
}