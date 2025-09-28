// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Api.Infrastructure.Grpc.Interceptors;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

public class RequestValidationInterceptor(RequestValidation validation) : OnRequestInterceptor {
    protected override ValueTask Intercept<TRequest>(TRequest request, ServerCallContext context) {
        validation.EnsureRequestIsValid(request);
        return ValueTask.CompletedTask;
    }
}
