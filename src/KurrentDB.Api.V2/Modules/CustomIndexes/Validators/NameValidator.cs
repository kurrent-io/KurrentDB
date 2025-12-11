// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Indexes.Custom;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class NameValidator : ValidatorBase<NameValidator, string?> {
	public NameValidator() =>
		RuleFor(x => x)
			.MinimumLength(1)
			.MaximumLength(1000)
			.Matches("^[a-z0-9_-]+$")
			.WithMessage("Name can contain only lowercase alphanumeric characters, underscores and dashes")
			.Must(NotBeReserved)
			.WithMessage("Name cannot be used as it's reserved")
			.WithName("Name");

	private static bool NotBeReserved(string name) {
		var streamName = CustomIndexHelpers.GetQueryStreamName(name);

		if (streamName is SystemStreams.DefaultSecondaryIndex)
			return false;

		if (streamName.StartsWith(SystemStreams.CategorySecondaryIndexPrefix))
			return false;

		if (streamName.StartsWith(SystemStreams.EventTypeSecondaryIndexPrefix))
			return false;

		return true;
	}
}
