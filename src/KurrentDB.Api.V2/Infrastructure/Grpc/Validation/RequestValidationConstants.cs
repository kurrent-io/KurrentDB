// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

static class RequestValidationConstants {
    public static readonly Type ValidatorOpenGenericType     = typeof(IRequestValidator<>);
    public static readonly Type ValidatorInterfaceMarkerType = typeof(IRequestValidator);
}
