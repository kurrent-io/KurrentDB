// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

/// <summary>
/// Provides validators for gRPC request types.
/// </summary>
public interface IRequestValidatorProvider {
    /// <summary>
    /// Tries to get a validator for the specified gRPC request type.
    /// </summary>
    public bool TryGetValidatorFor<TRequest>(out IRequestValidator validator);

    /// <summary>
    /// Gets a validator for the specified gRPC request type.
    /// Throws RequestValidatorNotFoundException if no validator is found.
    /// </summary>
    public IRequestValidator GetRequiredValidatorFor<TRequest>() =>
        TryGetValidatorFor<TRequest>(out var validator)
            ? validator
            : throw new RequestValidatorNotFoundException(typeof(TRequest));

    /// <summary>
    /// Gets a validator for the specified gRPC request type, or null if none is found.
    /// </summary>
    public IRequestValidator? GetValidatorFor<TRequest>() =>
        TryGetValidatorFor<TRequest>(out var validator)
            ? validator
            : null;
}

public sealed partial class RequestValidatorProvider : IRequestValidatorProvider {
    public RequestValidatorProvider(IEnumerable<IRequestValidator> validators, ILogger<RequestValidatorProvider> logger) {
        Validators = validators.ToFrozenDictionary(v => v.GetRequestType(), v => v);

        if (logger.IsEnabled(LogLevel.Debug)) {
            if (Validators.Count == 0)
                LogNoValidatorsRegistered(logger);
            else
                LogValidatorsRegistered(logger, Validators.Count, Validators.Values.Select(t => t.GetType().FullName!).ToArray());
        }
    }

    FrozenDictionary<Type, IRequestValidator> Validators { get; }

    public bool TryGetValidatorFor<TRequest>(out IRequestValidator validator) {
        if (Validators.TryGetValue(typeof(TRequest), out var instance)) {
            validator = instance;
            return true;
        }

        validator = null!;
        return false;
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "No gRPC validators registered", SkipEnabledCheck = true)]
    static partial void LogNoValidatorsRegistered(ILogger<RequestValidatorProvider> logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{ValidatorsCount} gRPC validators registered: {ValidatorTypes}", SkipEnabledCheck = true)]
    static partial void LogValidatorsRegistered(ILogger<RequestValidatorProvider> logger, int validatorsCount, string[] validatorTypes);

    #endregion
}
