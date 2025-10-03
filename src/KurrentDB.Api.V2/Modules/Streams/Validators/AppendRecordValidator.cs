// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class AppendRecordValidator : RequestValidator<AppendRecord> {
    public static readonly AppendRecordValidator Instance = new();

    public AppendRecordValidator() {
        RuleFor(x => x.RecordId)
            .SetValidator(RecordIdValidator.Instance)
            .When(x => x.HasRecordId);

        RuleFor(x => x.Schema.Format)
            .SetValidator(SchemaFormatValidator.Instance);

        RuleFor(x => x.Schema.Name)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.Schema.Id)
            .SetValidator(SchemaIdValidator.Instance)
            .When(x => x.Schema.HasId);
    }
}
