// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using FluentValidation.Results;
using Google.Protobuf;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

/// <summary>
/// Marker interface for gRPC request validators.
/// </summary>
public interface IRequestValidator : IValidator;

/// <summary>
/// Marker interface for gRPC request validators.
/// </summary>
/// <typeparam name="TRequest">
/// The type of the gRPC request to validate.
/// </typeparam>
public interface IRequestValidator<in TRequest> : IValidator<TRequest>, IRequestValidator where TRequest : IMessage;

/// <summary>
/// Base class for gRPC request validators.
/// </summary>
/// <typeparam name="TRequest">
/// The type of the gRPC request to validate.
/// </typeparam>
public abstract class RequestValidator<TRequest> : AbstractValidator<TRequest>, IRequestValidator<TRequest> where TRequest : IMessage;

public static class RequestValidatorExtensions {
    public static ValidationResult Validate<T>(this IRequestValidator validator, T request) =>
        validator.Validate(new ValidationContext<T>(request));

    public static Task<ValidationResult> ValidateAsync<T>(this IRequestValidator validator, T request, CancellationToken cancellationToken = default) =>
        validator.ValidateAsync(new ValidationContext<T>(request), cancellationToken);
}
