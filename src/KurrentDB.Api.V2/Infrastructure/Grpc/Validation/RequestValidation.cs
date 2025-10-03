// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

using Status = Google.Rpc.Status;
using Type   = System.Type;

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
        ValidatorProvider = validatorProvider;

        HandleValidatorNotFound = options.ThrowOnValidatorNotFound
            ? static t => throw CreateValidatorNotFoundException(t)
            : static _ => new ValidationResult();

        ExceptionFactory = options.ExceptionFactory ?? CreateValidationException;
    }

    IRequestValidatorProvider    ValidatorProvider       { get; }
    Func<Type, ValidationResult> HandleValidatorNotFound { get; }
    CreateValidationException    ExceptionFactory        { get; }

    /// <summary>
    /// Validates the given request using the appropriate validator.
    /// If no validator is found, it either throws an exception or returns a valid result based
    /// on the configured behavior.
    /// </summary>
    public ValidationResult ValidateRequest<TRequest>(TRequest request) =>
        ValidatorProvider.GetValidatorFor<TRequest>() is { } validator
            ? validator.Validate(request)
            : HandleValidatorNotFound(typeof(TRequest));

    /// <summary>
    /// Ensures that the given request is valid.
    /// If the request is invalid, throws an exception created by the configured ExceptionFactory.
    /// </summary>
    public TRequest EnsureRequestIsValid<TRequest>(TRequest request) {
        var result = ValidateRequest(request);
        return result.IsValid ? request : throw ExceptionFactory(typeof(TRequest), result.Errors);
    }

    /// <summary>
    /// Exception thrown when a gRPC request fails validation.
    /// </summary>
    static RpcException CreateValidationException(Type requestType, List<ValidationFailure> errors) {
        var violations = errors.Select(failure => new BadRequest.Types.FieldViolation {
            Field       = failure.PropertyName,
            Description = failure.ErrorMessage
        });

        var details = new BadRequest { FieldViolations = { violations } };

        var message = $"gRPC request {requestType.Name} is invalid! See attached details for more information.";

        var status = new Status {
            Code    = (int)Code.InvalidArgument,
            Message = message,
            Details = { Any.Pack(details) }
        };

        return status.ToRpcException();
    }

    /// <summary>
    /// Exception thrown when no validator is found for a gRPC request type and ThrowOnNoValidator is true.
    /// </summary>
    static RpcException CreateValidatorNotFoundException(Type requestType) {
        var message = $"gRPC validator for {requestType.Name} was not found!";

        var status = new Status {
            Code    = (int)Code.Internal,
            Message = message,
        };

        return status.ToRpcException();
    }
}
