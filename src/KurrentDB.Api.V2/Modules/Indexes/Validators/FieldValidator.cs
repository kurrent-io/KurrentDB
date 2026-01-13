// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Modules.Indexes.Validators;

class FieldValidator : ValidatorBase<FieldValidator, IndexField> {
	public FieldValidator() {
		RuleFor(x => x.Name)
			.SetValidator(IndexNameValidator.Instance);

		RuleFor(x => x.Selector)
			.Must(x => JsFunctionValidator.IsValidFunctionWithOneArgument(x))
			.WithMessage("Field selector must be a valid JavaScript function with exactly one argument")
			.WithName("Field selector");

		RuleFor(x => x.Type)
			.Must(x => x is not IndexFieldType.Unspecified)
			.WithMessage("Field type must be specified")
			.WithName("Field type");
	}
}
