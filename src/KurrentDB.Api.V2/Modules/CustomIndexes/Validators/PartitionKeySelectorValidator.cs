// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class PartitionKeySelectorValidator : ValidatorBase<PartitionKeySelectorValidator, string?> {
	public PartitionKeySelectorValidator() =>
		RuleFor(x => x)
			.Must(x => x is "" || JsFunctionValidator.IsValidFunctionWithOneArgument(x))
			.WithMessage("Partition key selector must be empty or a valid JavaScript function with exactly one argument")
			.WithName("Partition key selector");
}
