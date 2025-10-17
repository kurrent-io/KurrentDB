// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation.Results;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

/// <summary>
/// Options for configuring gRPC request validation
/// </summary>
[PublicAPI]
public class RequestValidationOptions {
    /// <summary>
    /// If true, an exception will be thrown if no validator is found for a given request type.
    /// If false, the request will be considered valid if no validator is found.
    /// Default is false.
    /// </summary>
    public bool ThrowOnValidatorNotFound { get; set; }

    /// <summary>
    /// Log level to use when no validator is found for a request type and ThrowOnValidatorNotFound is false.
    /// Default is Debug.
    /// </summary>
    public LogLevel ValidatorNotFoundLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Factory for creating exceptions when calling EnsureRequestIsValid and validation fails.
    /// </summary>
    public CreateValidationException? ExceptionFactory { get; set; }
}

/// <summary>
/// Delegate for creating validation exceptions.
/// </summary>
public delegate RpcException CreateValidationException(Type requestType, List<ValidationFailure> errors);
