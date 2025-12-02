// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

class DisableCustomIndexValidator : RequestValidator<DisableCustomIndexRequest> {
    public static readonly DisableCustomIndexValidator Instance = new();

    private DisableCustomIndexValidator() {
	    RuleFor(x => x.Name)
		    .SetValidator(NameValidator.Instance);
    }
}
