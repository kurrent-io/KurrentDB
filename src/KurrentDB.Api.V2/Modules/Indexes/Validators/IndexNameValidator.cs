// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Modules.Indexes.Validators;

// Lower case restriction because identifiers are case-insensitive in duckdb
class IndexNameValidator : ValidatorBase<IndexNameValidator, string?> {
	public IndexNameValidator() =>
		RuleFor(x => x)
			.MinimumLength(1)
			.MaximumLength(1000)
			.Matches("^[a-z0-9_-]+$")
			.WithMessage("Name can contain only lowercase alphanumeric characters, underscores and dashes")
			.WithName("Name");
}
