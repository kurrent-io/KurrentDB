// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;

namespace KurrentDB.Api.Streams.Validators;

partial class StreamNameValidator : AbstractValidator<string?> {
	public static readonly StreamNameValidator Instance = new();

	public StreamNameValidator() =>
		RuleFor(x => x)
			.NotEmpty()
			.Matches(RegEx())
			.WithMessage("Stream name must not be empty and can only contain alphanumeric characters, underscores, dashes, and periods");

	[System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z0-9_.-]+$")]
	private static partial System.Text.RegularExpressions.Regex RegEx();
}
