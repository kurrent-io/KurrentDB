// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Modules.Indexes.Validators;

class StopIndexValidator : RequestValidator<StopIndexRequest> {
	public static readonly StopIndexValidator Instance = new();

	private StopIndexValidator() {
		RuleFor(x => x.Name)
			.SetValidator(NameValidator.Instance);
	}
}
