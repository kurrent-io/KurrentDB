using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

/// <summary>
/// Options for configuring gRPC request validation
/// </summary>
[PublicAPI]
public record RequestValidationOptions {
    /// <summary>
    /// If true, an exception will be thrown if no validator is found for a given request type.
    /// If false, the request will be considered valid if no validator is found.
    /// Default is false.
    /// </summary>
    public bool ThrowOnValidatorNotFound { get; init; }

    /// <summary>
    /// Log level to use when no validator is found for a request type and ThrowOnValidatorNotFound is false.
    /// Default is Debug.
    /// </summary>
    public LogLevel ValidatorNotFoundLogLevel { get; init; } = LogLevel.Debug;

    /// <summary>
    /// Factory for creating exceptions when calling EnsureRequestIsValid and validation fails.
    /// </summary>
    public CreateValidationException ExceptionFactory { get; init; } = DefaultExceptionFactory;

    static CreateValidationException DefaultExceptionFactory =>
        static (requestType, errors) => new InvalidRequestException(requestType, errors);
}

/// <summary>
/// Delegate for creating validation exceptions.
/// </summary>
public delegate Exception CreateValidationException(Type requestType, IReadOnlyList<ValidationFailure> errors);
