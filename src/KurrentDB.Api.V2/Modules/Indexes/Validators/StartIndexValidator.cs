// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Modules.Indexes.Validators;

class StartIndexValidator : RequestValidator<StartIndexRequest> {
	public static readonly StartIndexValidator Instance = new();

	private StartIndexValidator() {
		RuleFor(x => x.Name)
			.SetValidator(IndexNameValidator.Instance);
	}
}
