// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class FieldValidator : ValidatorBase<FieldValidator, Field> {
	public FieldValidator() {
		RuleFor(x => x.Name)
			.SetValidator(NameValidator.Instance); //qq this requires lower case but we don't necesarily want to. probably put our own validation here

		RuleFor(x => x.Selector)
			.Must(x => x is "" || JsFunctionValidator.IsValidFunctionWithOneArgument(x))
			.WithMessage("Field selector must be empty or a valid JavaScript function with exactly one argument")
			.WithName("Field selector");

		//qq RuleFor(x => x.Type);
	}
}
