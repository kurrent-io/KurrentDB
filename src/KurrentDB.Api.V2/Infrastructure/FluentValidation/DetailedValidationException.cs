// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation.Results;

namespace KurrentDB.Api.Infrastructure.FluentValidation;

/// <summary>
/// Exception thrown when validation fails, containing detailed information about the validation errors.
/// </summary>
/// <param name="instanceType">
/// The type of the instance that failed validation.
/// </param>
/// <param name="errors">
/// The validation errors that caused the exception.
/// </param>
public class DetailedValidationException(Type instanceType, params ValidationFailure[] errors) : Exception(BuildErrorMessage(instanceType, errors)) {
    /// <summary>
    /// The type of the instance that failed validation.
    /// </summary>
    public Type InstanceType { get; } = instanceType;

    /// <summary>
    /// The validation errors that caused the exception.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Errors { get; } = errors;

    static string BuildErrorMessage(Type instanceType, IEnumerable<ValidationFailure> errors) {
        var arr = errors.Select(x => $"{Environment.NewLine} -- {x.ErrorMessage}");
        return $"{instanceType.Name} validation failed: {string.Join(string.Empty, arr)}";
    }
}
