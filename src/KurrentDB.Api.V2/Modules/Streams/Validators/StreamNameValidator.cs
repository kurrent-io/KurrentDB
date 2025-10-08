// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Streams.Validators;

class StreamNameValidator : ValidatorBase<StreamNameValidator, string?> {
	public StreamNameValidator() =>
        RuleFor(x => x)
            .NotEmpty()
            .NotEqual("$$")
            .WithMessage("'{PropertyName}' must not be '$$'.")
            .WithName("Stream");
}
