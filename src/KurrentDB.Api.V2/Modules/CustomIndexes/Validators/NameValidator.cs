// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class NameValidator : ValidatorBase<NameValidator, string?> {
	public NameValidator() =>
		RuleFor(x => x)
			.Matches("^[a-z0-9_-]+$")
			.WithMessage("Name can contain only lowercase alphanumeric characters, underscores and dashes")
			.WithName("Name");
}
