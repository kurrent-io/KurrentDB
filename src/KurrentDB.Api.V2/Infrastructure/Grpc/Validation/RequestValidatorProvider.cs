// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;
using FluentStorage.Utils.Extensions;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

/// <summary>
/// Provides validators for gRPC request types.
/// </summary>
public interface IRequestValidatorProvider {
    /// <summary>
    /// Gets a validator for the specified gRPC request type, or null if none is found.
    /// </summary>
    IRequestValidator? GetValidatorFor<TRequest>();
}

public sealed class RequestValidatorProvider : IRequestValidatorProvider {
    public static RequestValidatorProviderBuilder New() => new();

    public RequestValidatorProvider(IEnumerable<IRequestValidator> validators) =>
        Validators = validators.ToFrozenDictionary(v => v.GetRequestType(), v => v);

    public RequestValidatorProvider(Dictionary<Type, IRequestValidator> validators) =>
        Validators = validators.ToFrozenDictionary();

    FrozenDictionary<Type, IRequestValidator> Validators { get; }

    public IRequestValidator? GetValidatorFor<TRequest>() =>
        Validators.TryGetValue(typeof(TRequest), out var instance) ? instance : null;
}

public class RequestValidatorProviderBuilder {
    Dictionary<Type, IRequestValidator> Validators { get; } = new();

    public RequestValidatorProviderBuilder AddValidator<TValidator>(TValidator validator) where TValidator : class, IRequestValidator {
        // the type clauses above are not enough it must also implement IRequestValidator<>
        // otherwise someone could register a class that implements only IRequestValidator
        // which is not useful
        var validatorType = typeof(TValidator);

        if (!validatorType.IsRequestValidator())
            throw new InvalidOperationException($"The type {validatorType.FullName} is not a valid gRPC request validator");

        var requestType = validatorType.GetRequestType();
        return !Validators.TryAdd(requestType, validator)
            ? throw new InvalidOperationException($"A validator for request type {requestType.FullName} has already been registered")
            : this;
    }

    public RequestValidatorProviderBuilder AddValidator<TValidator>(object[] args) where TValidator : class, IRequestValidator {
        if (args.Length == 0)
            throw new ArgumentException("Validator constructor arguments cannot be empty", nameof(args));

        TValidator validator;

        try {
            validator = (TValidator)Activator.CreateInstance(typeof(TValidator), args)!;
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Could not create an instance of validator type {typeof(TValidator).FullName}. {ex.InnerException?.Message ?? ex.Message}", ex);
        }

        return AddValidator(validator);
    }

    public RequestValidatorProviderBuilder AddValidator<TValidator>() where TValidator : class, IRequestValidator, new() =>
        AddValidator(new TValidator());

    public RequestValidatorProviderBuilder AddValidators(IEnumerable<IRequestValidator> validators) {
        Validators.AddRange(validators.ToDictionary(v => v.GetRequestType(), v => v));
        return this;
    }

    public IRequestValidatorProvider Build() =>
        new RequestValidatorProvider(Validators);
}

// public sealed partial class RequestValidatorProvider : IRequestValidatorProvider {
//     public RequestValidatorProvider(IEnumerable<IRequestValidator> validators, ILogger<RequestValidatorProvider> logger) {
//         Validators = validators.ToFrozenDictionary(v => v.GetRequestType(), v => v);
//
//     }
//
//     public RequestValidatorProvider(Dictionary<Type, IRequestValidator> validators, ILogger<RequestValidatorProvider> logger)  {
//         Validators = validators.ToFrozenDictionary();
//
//         if (logger.IsEnabled(LogLevel.Debug)) {
//             if (Validators.Count == 0)
//                 LogNoValidatorsRegistered(logger);
//             else
//                 LogValidatorsRegistered(logger, Validators.Count, Validators.Values.Select(t => t.GetType().FullName!).ToArray());
//         }
//     }
//
//     FrozenDictionary<Type, IRequestValidator> Validators { get; }
//
//     public bool TryGetValidatorFor<TRequest>(out IRequestValidator validator) {
//         if (Validators.TryGetValue(typeof(TRequest), out var instance)) {
//             validator = instance;
//             return true;
//         }
//
//         validator = null!;
//         return false;
//     }
//
//     #region Logging
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "No gRPC validators registered", SkipEnabledCheck = true)]
//     static partial void LogNoValidatorsRegistered(ILogger<RequestValidatorProvider> logger);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "{ValidatorsCount} gRPC validators registered: {ValidatorTypes}", SkipEnabledCheck = true)]
//     static partial void LogValidatorsRegistered(ILogger<RequestValidatorProvider> logger, int validatorsCount, string[] validatorTypes);
//
//     #endregion
// }
