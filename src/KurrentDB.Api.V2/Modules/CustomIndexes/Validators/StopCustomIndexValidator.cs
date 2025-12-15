// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class StopCustomIndexValidator : RequestValidator<StopIndexRequest> {
	public static readonly StopCustomIndexValidator Instance = new();

	private StopCustomIndexValidator() {
		RuleFor(x => x.Name)
			.SetValidator(NameValidator.Instance);
	}
}
