// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Streams.Validators;

class RecordIdValidator : ValidatorBase<RecordIdValidator, string?> {
	public RecordIdValidator() =>
		RuleFor(x => x)
			.Must(value => Guid.TryParse(value, out var valueGuid) && valueGuid != Guid.Empty)
			.WithMessage("{PropertyName} must be a valid and non-empty UUID.")
            .WithName("Record ID");
}
