// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

static class RequestValidationTypeExtensions {
    public static bool IsRequestValidator(this Type type) {
        return type is { IsClass: true, IsAbstract: false }
            && type.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == RequestValidationConstants.ValidatorOpenGenericType);
    }

    public static Type GetRequestType(this Type validatorType) =>
        validatorType.TryGetRequestType(out var requestType)
            ? requestType
            : throw new InvalidOperationException($"The type {validatorType.FullName} is not a valid gRPC request validator");

    public static Type GetRequestType(this object validatorInstance) =>
        validatorInstance.GetType().GetRequestType();

    public static TypeInfo GetValidatorTypeInfo(Type requestType) =>
        typeof(IRequestValidator<>).MakeGenericType(requestType).GetTypeInfo();

    static bool TryGetRequestType(this Type validatorType, out Type requestType) {
        var matchingInterface = validatorType.GetInterfaces()
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == RequestValidationConstants.ValidatorOpenGenericType);

        if (matchingInterface is not null) {
            requestType = matchingInterface.GenericTypeArguments[0];
            return true;
        }

        requestType = null!;
        return false;
    }
}
