// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using Google.Protobuf;
using KurrentDB.Api.Errors;

namespace KurrentDB.Api.Infrastructure.Validation;

static class ValidatorExtensions {
    public static T EnsureValid<T>(this IValidator<T> validator, T request) where T : IMessage {
        var result = validator.Validate(request);
        return !result.IsValid ? throw ApiErrors.InvalidRequest(result) : request;
    }
}
