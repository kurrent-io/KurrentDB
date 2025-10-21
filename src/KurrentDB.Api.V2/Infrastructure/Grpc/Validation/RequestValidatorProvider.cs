// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;

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
    public RequestValidatorProvider(IEnumerable<IRequestValidator> validators) =>
        Validators = validators.ToFrozenDictionary(v => v.RequestType, v => v);

    public RequestValidatorProvider(Dictionary<Type, IRequestValidator> validators) =>
        Validators = validators.ToFrozenDictionary();

    FrozenDictionary<Type, IRequestValidator> Validators { get; }

    public IRequestValidator? GetValidatorFor<TRequest>() =>
        Validators.TryGetValue(typeof(TRequest), out var instance) ? instance : null;
}
