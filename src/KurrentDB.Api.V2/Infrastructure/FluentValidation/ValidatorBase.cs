// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using FluentValidation.Results;

namespace KurrentDB.Api.Infrastructure.FluentValidation;

/// <summary>
/// Base class for validators that provides a singleton instance and throws DetailedValidationException on failure.
/// </summary>
/// <typeparam name="TValidator">
/// The type of the validator itself. This is used to create a singleton instance.
/// </typeparam>
/// <typeparam name="TInstance">
/// The type of the instance being validated.
/// </typeparam>
public abstract class ValidatorBase<TValidator, TInstance> : AbstractValidator<TInstance> where TValidator : ValidatorBase<TValidator, TInstance>, new() {
    /// <summary>
    /// Singleton instance of the validator.
    /// </summary>
    public static readonly TValidator Instance = new();

    protected ValidatorBase() {
        ValidatorType = GetType();
        InstanceType  = typeof(TInstance);
    }

    /// <summary>
    /// The type of the validator.
    /// </summary>
    public Type ValidatorType { get; }

    /// <summary>
    /// The type of the instance being validated.
    /// </summary>
    public Type InstanceType  { get; }

    protected override void RaiseValidationException(ValidationContext<TInstance> context, ValidationResult result) =>
        throw new DetailedValidationException(context.DisplayName, typeof(TInstance), result.Errors.ToArray());
}
