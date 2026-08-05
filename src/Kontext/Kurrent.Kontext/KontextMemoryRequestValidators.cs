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

        // A WebRef without an excerpt is a bookmark, not a citation: the page is the one cited source
        // that can be deleted or rewritten out from under us, so the quoted passage IS the anchor.
        // Requiring it also means a citation cannot be produced without having read the source, which
        // is what stops an unverified claim dressing itself up with an unread link.
        RuleForEach(x => x.Memories)
            .Must(m => m.Evidence.Where(e => e.Web is not null).All(e => MemoryRequestRules.ValidWebExcerpts(e.Web)))
            .WithMessage($"A web citation requires 1..{MemoryRequestRules.MaxWebExcerpts} excerpts, each {MemoryRequestRules.MinExcerptLength}..{MemoryRequestRules.MaxExcerptLength} characters.");

        RuleForEach(x => x.Memories)
            .Must(m => m.Evidence.Where(e => e.Git is not null).All(e => MemoryRequestRules.ValidGitExcerpt(e.Git)))
            .WithMessage($"A git citation's excerpt is optional but, when set, must be {MemoryRequestRules.MinExcerptLength}..{MemoryRequestRules.MaxExcerptLength} characters.");

        // HEARSAY is the unverified claim — the residue with neither derivation nor verification. A
        // memory citation would make it a derived inference; a source citation would mean you checked
        // it, at which point it has become an OBSERVATION or a FACT. Enforced rather than merely
        // documented, because this is the memory-hacking guard: a citation is exactly how a
        // fabricated claim would dress itself up as trustworthy.
        RuleForEach(x => x.Memories)
            .Must(m => m.MemoryType != Contracts.MemoryType.Hearsay || m.Evidence.Count == 0)
            .WithMessage("A HEARSAY memory carries no evidence — verify the claim and retain a FACT that supersedes it.");
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
    // An excerpt below the floor ("yes") satisfies "at least one" while carrying no evidence; above the
    // ceiling it stops being a passage and becomes a copy of the source.
    public const int MinExcerptLength = 20;
    public const int MaxExcerptLength = 1000;
    public const int MaxWebExcerpts   = 5;

    public static bool Guid(string value) => global::System.Guid.TryParse(value, out _);

    public static bool EmptyOrGuid(string value) => string.IsNullOrEmpty(value) || global::System.Guid.TryParse(value, out _);

    public static bool ValidWebExcerpts(Contracts.Evidence.Types.WebRef web) =>
        web.Excerpts.Count > 0
     && web.Excerpts.Count <= MaxWebExcerpts
     && web.Excerpts.All(WithinExcerptBounds);

    public static bool ValidGitExcerpt(Contracts.Evidence.Types.GitRef git) =>
        string.IsNullOrEmpty(git.Excerpt) || WithinExcerptBounds(git.Excerpt);

    static bool WithinExcerptBounds(string excerpt) =>
        excerpt.Length >= MinExcerptLength && excerpt.Length <= MaxExcerptLength;
}