// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Validation;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class AppendRequestValidator : EnhancedValidator<AppendRequest> {
	public static readonly AppendRequestValidator Instance = new();

	public AppendRequestValidator() {
		RuleFor(x => x.Stream)
			.SetValidator(StreamNameValidator.Instance);

		RuleFor(x => x.Records)
			.NotEmpty()
			.WithMessage("Stream append request must contain at least one record");
	}
}
