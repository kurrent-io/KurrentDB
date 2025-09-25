// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation.Results;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

/// <summary>
/// Central request validation service for gRPC requests.
/// It uses IRequestValidatorProvider to find the appropriate validator for each request type.
/// If no validator is found, it either throws an exception or logs a warning based on the options.
/// </summary>
[PublicAPI]
public sealed class RequestValidation {
    /// <summary>
    /// Creates a new instance of RequestValidation.
    /// </summary>
    /// <param name="options">
    /// The options to configure the behavior of the validation service.
    /// </param>
    /// <param name="validatorProvider">
    /// The provider to get validators for gRPC request types.
    /// </param>
    public RequestValidation(RequestValidationOptions options, IRequestValidatorProvider validatorProvider) {
        Options           = options;
        ValidatorProvider = validatorProvider;

        HandleValidatorNotFound = options.ThrowOnValidatorNotFound
            ? static t => throw new RequestValidatorNotFoundException(t)
            : static _ => new ValidationResult();
    }

    RequestValidationOptions     Options                 { get; }
    IRequestValidatorProvider    ValidatorProvider       { get; }
    Func<Type, ValidationResult> HandleValidatorNotFound { get; }

    public ValidationResult ValidateRequest<TRequest>(TRequest request) {
        if (ValidatorProvider.GetValidatorFor<TRequest>() is { } validator)
            return validator.Validate(request);

        return HandleValidatorNotFound(typeof(TRequest));
    }

    /// <summary>
    /// Ensures that the given request is valid.
    /// If the request is invalid, throws an exception created by the configured ExceptionFactory.
    /// </summary>
    public TRequest EnsureRequestIsValid<TRequest>(TRequest request) {
        var result = ValidateRequest(request);
        return result.IsValid ? request : throw Options.ExceptionFactory(typeof(TRequest), result.Errors);
    }
}

/// <summary>
/// Exception thrown when no validator is found for a gRPC request type and ThrowOnNoValidator is true.
/// </summary>
public class RequestValidatorNotFoundException(Type requestType)
    : Exception($"gRPC request {requestType.Name} validator not found! Ensure a validator is registered for this request type.");

/// <summary>
/// Exception thrown when a gRPC request validation fails.
/// </summary>
/// <param name="requestType"></param>
/// <param name="errors"></param>
public class InvalidRequestException(Type requestType, IReadOnlyList<ValidationFailure> errors) : Exception(ErrorMessage(requestType)) {
    public IReadOnlyList<ValidationFailure> Errors { get; } = errors;

    static string ErrorMessage(Type requestType) =>
        $"gRPC request {requestType.Name} is invalid! See Errors property for details.";
}
