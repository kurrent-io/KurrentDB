// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class ValueSelectorValidator : ValidatorBase<ValueSelectorValidator, string?> {
	public ValueSelectorValidator() =>
		RuleFor(x => x)
			.Must(x => x is "" || JsFunctionValidator.IsValidFunctionWithOneArgument(x))
			.WithMessage("ValueSelector must be empty or a valid JavaScript function with exactly one argument")
			.WithName("ValueSelector");
}
