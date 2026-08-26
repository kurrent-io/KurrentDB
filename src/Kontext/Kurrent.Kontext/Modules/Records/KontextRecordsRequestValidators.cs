// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;

namespace Kurrent.Kontext.Records;

// `scope` and `schema` are protobuf oneofs, so their exclusivity is structural — setting the second
// field clears the first before a validator could ever see both.
public sealed class SearchRequestValidator : RequestValidator<Contracts.SearchRequest> {
    public SearchRequestValidator() {
        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("query is required.");

        RuleFor(x => x.Limit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("limit must not be negative.");

        RuleFor(x => x.MinScore)
            .GreaterThanOrEqualTo(0)
            .WithMessage("min_score must not be negative.");
    }
}

// What the SQL may touch is the query engine's rule, not this validator's: it parses the statement
// and rejects any table outside its allowlist. Re-deciding that here would be a second, drifting copy.
public sealed class QueryRequestValidator : RequestValidator<Contracts.QueryRequest> {
    public QueryRequestValidator() {
        RuleFor(x => x.Sql)
            .NotEmpty()
            .WithMessage("sql is required.");

        RuleFor(x => x.Limit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("limit must not be negative.");
    }
}
