// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;

namespace KurrentDB.Api.Streams.Validators;

class StreamNameValidator : AbstractValidator<string?> {
	public static readonly StreamNameValidator Instance = new();

    public StreamNameValidator() =>
        RuleFor(x => x)
            .NotEmpty()
            .WithName("Stream name")
            .NotEqual("$$")
            .WithMessage("'Stream name' must not be '$$'.");
}
