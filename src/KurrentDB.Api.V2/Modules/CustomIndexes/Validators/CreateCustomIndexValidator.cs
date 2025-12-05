// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class CreateCustomIndexValidator : RequestValidator<CreateCustomIndexRequest> {
	public static readonly CreateCustomIndexValidator Instance = new();

	private CreateCustomIndexValidator() {
		RuleFor(x => x.Name)
			.SetValidator(NameValidator.Instance);

		RuleFor(x => x.Filter)
			.SetValidator(FilterValidator.Instance);

		RuleFor(x => x.PartitionKeySelector)
			.SetValidator(PartitionKeySelectorValidator.Instance)
			.When(x => x.PartitionKeySelector != string.Empty);
	}
}
