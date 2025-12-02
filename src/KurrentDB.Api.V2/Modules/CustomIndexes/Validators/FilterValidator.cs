// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class FilterValidator : ValidatorBase<FilterValidator, string?> {
	public FilterValidator() =>
		RuleFor(x => x)
			.Must(JsFunctionValidator.IsValidFunctionWithOneArgument)
			.WithMessage("Filter must be a valid JavaScript function with exactly one argument")
			.WithName("Filter");
}
