// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using FluentValidation.Results;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

namespace KurrentDB.SchemaRegistry.Infrastructure.Grpc;

public static class RpcExceptions {
    static RpcException Create(StatusCode statusCode, string message, IMessage? detail = null) {
        if (detail is not null) {
            return RpcStatusExtensions.ToRpcException(new() {
                Code    = (int)statusCode,
                Message = message,
                Details = { Any.Pack(detail) }
            });
        }

        return RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)statusCode,
            Message = message
        });
    }

    public static RpcException InvalidArgument(ValidationResult validationResult) =>
        InvalidArgument(validationResult.ToDictionary());

    public static RpcException InvalidArgument(Exception exception) =>
        Create(StatusCode.InvalidArgument, exception.Message);

    public static RpcException InvalidArgument(IEnumerable<ValidationFailure> errors) {
        var failures = errors
            .GroupBy(x => x.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray());

        var details = new BadRequest {
            FieldViolations = {
                failures.Select(failure => new BadRequest.Types.FieldViolation {
                    Field       = failure.Key,
                    Description = failure.Value.Aggregate((a, b) => $"{a}, {b}")
                })
            }
        };

        var message = JsonSerializer.Serialize(failures);

        return Create(StatusCode.InvalidArgument, message, details);
    }

    public static RpcException InvalidArgument(IDictionary<string, string[]> failures) {
        var details = new BadRequest {
            FieldViolations = {
                failures.Select(failure => new BadRequest.Types.FieldViolation {
                    Field       = failure.Key,
                    Description = failure.Value.Aggregate((a, b) => $"{a}, {b}")
                })
            }
        };

        var message = JsonSerializer.Serialize(failures);

        return Create(StatusCode.InvalidArgument, message, details);
    }

    public static RpcException FailedPrecondition(string errorMessage) =>
        Create(StatusCode.FailedPrecondition, errorMessage);

    public static RpcException FailedPrecondition(Exception exception) =>
        Create(StatusCode.FailedPrecondition, exception.Message);

    public static RpcException FailedPrecondition(IDictionary<string, string[]> failures) {
        var details = new PreconditionFailure {
            Violations = {
                failures.Select(failure => new PreconditionFailure.Types.Violation {
                    Subject     = failure.Key,
                    Description = failure.Value.Aggregate((a, b) => $"{a}, {b}")
                })
            }
        };

        var message = JsonSerializer.Serialize(failures);

        return Create(StatusCode.FailedPrecondition, message, details);
    }

    public static RpcException OutOfRange(IDictionary<string, string[]> failures) {
        var details = new BadRequest {
            FieldViolations = {
                failures.Select(failure => new BadRequest.Types.FieldViolation {
                    Field       = failure.Key,
                    Description = failure.Value.Aggregate((a, b) => $"{a}, {b}")
                })
            }
        };

        var message = JsonSerializer.Serialize(failures);

        return Create(StatusCode.OutOfRange, message, details);
    }

    public static RpcException Unauthenticated(IDictionary<string, string> metadata) {
        var details = new ErrorInfo {
            Reason   = "UNAUTHENTICATED",
            Domain   = "authentication",
            Metadata = { metadata }
        };

        var message = JsonSerializer.Serialize(metadata);

        return Create(StatusCode.Unauthenticated, message, details);
    }

    public static RpcException PermissionDenied(Exception? exception = null) {
        var errorInfo = new ErrorInfo {
            Reason = "PERMISSION_DENIED",
            Domain = "authorization"
        };

        return Create(StatusCode.PermissionDenied, exception?.Message ?? "Permission denied", errorInfo);
    }

    public static RpcException PermissionDenied(IDictionary<string, string[]> failures, string domain = "authorization") {
        var errorInfo = new ErrorInfo {
            Reason = "PERMISSION_DENIED",
            Domain = domain,
            Metadata = {
                failures.ToDictionary(failure => failure.Key,
                                      failure => string.Join(", ", failure.Value))
            }
        };

        var message = JsonSerializer.Serialize(failures);

        return Create(StatusCode.PermissionDenied, message, errorInfo);
    }

    public static RpcException NotFound(Exception ex) {
        var resourceInfo = new ResourceInfo {
            Description = ex.Message
        };

        return Create(StatusCode.NotFound, ex.Message, resourceInfo);
    }

    public static RpcException NotFound(string resourceType, string resourceName) {
        var description = $"The resource '{resourceType}' named '{resourceName}' was not found.";

        var resourceInfo = new ResourceInfo {
            ResourceType = resourceType,
            ResourceName = resourceName,
            Description  = description
        };

        return Create(StatusCode.NotFound, description, resourceInfo);
    }

    public static RpcException NotFound(string resourceType, string resourceName, string resourceOwner, string? message = null) {
        var description = message ?? $"The resource '{resourceType}' named '{resourceName}' was not found.";

        var resourceInfo = new ResourceInfo {
            ResourceType = resourceType,
            ResourceName = resourceName,
            Owner        = resourceOwner,
            Description  = description
        };

        return Create(StatusCode.NotFound, description, resourceInfo);
    }

    public static RpcException Aborted(TimeSpan? retryDelay = null) {
        var retryInfo = new RetryInfo {
            RetryDelay = retryDelay.HasValue ? Duration.FromTimeSpan(retryDelay.Value) : null
        };

        return Create(StatusCode.Aborted, "The operation was aborted due to a concurrency conflict.", retryInfo);
    }

    public static RpcException AlreadyExists(Exception exception) {
        var resourceInfo = new ResourceInfo {
            Description = exception.Message
        };

        return Create(StatusCode.AlreadyExists, exception.Message, resourceInfo);
    }

    public static RpcException AlreadyExists(
        string resourceType, string resourceName, string resourceOwner, string? message = null
    ) {
        var description = message ?? $"The resource '{resourceType}' named '{resourceName}' already exists.";

        var resourceInfo = new ResourceInfo {
            ResourceType = resourceType,
            ResourceName = resourceName,
            Owner        = resourceOwner,
            Description  = description
        };

        return Create(StatusCode.AlreadyExists, description, resourceInfo);
    }

    public static RpcException ResourceExhausted(IEnumerable<QuotaFailure.Types.Violation> violations) {
        var quotaFailure = new QuotaFailure {
            Violations = { violations }
        };

        return Create(StatusCode.ResourceExhausted, "Resource limits have been exceeded.", quotaFailure);
    }

    public static RpcException Cancelled(string reason = "Operation cancelled by the client.") {
        var errorInfo = new ErrorInfo {
            Reason   = "OPERATION_CANCELLED",
            Domain   = "client",
            Metadata = { { "details", reason } }
        };

        return Create(StatusCode.Cancelled, reason, errorInfo);
    }

    public static RpcException DataLoss(
        string detail = "Unrecoverable data loss or corruption.", IEnumerable<string>? stackEntries = null
    ) {
        var debugInfo = new DebugInfo {
            Detail       = detail,
            StackEntries = { stackEntries ?? [] }
        };

        return Create(StatusCode.DataLoss, detail, debugInfo);
    }

    public static RpcException Unknown(
        string detail = "An unknown error occurred.", IEnumerable<string>? stackEntries = null
    ) {
        var debugInfo = new DebugInfo {
            Detail       = detail,
            StackEntries = { stackEntries ?? [] }
        };

        return Create(StatusCode.Unknown, detail, debugInfo);
    }

    public static RpcException Internal(Exception ex) {
        var detail = ex.Message;

        var debugInfo = new DebugInfo {
            Detail       = detail,
            // StackEntries = { ex.GetFullStackTraceList() ex.StackTrace?.Split(Environment.NewLine) ?? Enumerable.Empty<string>() }
            StackEntries = { ex.GetFullStackTraceList() }
        };

        return Create(StatusCode.Internal, detail, debugInfo);
    }

    public static RpcException Unimplemented(string methodName, string? detail = null) {
        var errorInfo = new ErrorInfo {
            Reason   = "METHOD_NOT_IMPLEMENTED",
            Domain   = "server",
            Metadata = { { "method", methodName } }
        };

        var message = detail ?? $"The method '{methodName}' is not implemented.";

        return Create(StatusCode.Unimplemented, message, errorInfo);
    }

    public static RpcException Unavailable(TimeSpan? retryDelay = null) {
        var retryInfo = new RetryInfo {
            RetryDelay = retryDelay.HasValue ? Duration.FromTimeSpan(retryDelay.Value) : null
        };

        return Create(StatusCode.Unavailable, "The service is currently unavailable.", retryInfo);
    }

    public static RpcException DeadlineExceeded(TimeSpan? retryDelay = null) {
        var retryInfo = new RetryInfo {
            RetryDelay = retryDelay.HasValue ? Duration.FromTimeSpan(retryDelay.Value) : null
        };

        return Create(StatusCode.DeadlineExceeded, "The deadline for the operation was exceeded.", retryInfo);
    }
}

public static class ExceptionExtensions {
    // Pre-allocate the delimiter array to avoid allocating it inside the loop.
    static readonly string[] NewlineDelimiters = ["\r\n", "\r", "\n"];

    // Consider a reasonable initial capacity if you often have large traces.
    // Adjust based on typical usage patterns. 16 is List<T>'s default.
    const int InitialListCapacity = 32;

    /// <summary>
    /// Gets the full stack trace from an exception, including all inner exceptions,
    /// formatted as a list of non-empty, trimmed strings. Optimized for fewer allocations.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns>A list of strings representing the combined stack trace lines.</returns>
    public static List<string> GetFullStackTraceList(this Exception? ex) {
        if (ex is null)
            return [];

        // Start with a slightly larger capacity if traces are often non-trivial
        var stackTraceLines = new List<string>(InitialListCapacity);

        var currentException = ex;
        var exceptionLevel   = 0;

        while (currentException is not null) {
            // Add header - string interpolation allocates, but it's clear and
            // typically not the biggest factor compared to splitting stack traces.
            var header = exceptionLevel > 0
                ? $"--- Inner Exception Level {exceptionLevel} ({currentException.GetType().FullName}) ---"
                : $"--- Exception ({currentException.GetType().FullName}) ---";

            stackTraceLines.Add(header);

            // Add the message for context
            stackTraceLines.Add($"Message: {currentException.Message}");

            var stackTrace = currentException.StackTrace;

            if (!string.IsNullOrWhiteSpace(stackTrace)) {
                // Use the pre-allocated delimiters array.
                // StringSplitOptions.RemoveEmptyEntries | TrimEntries is efficient.
                // This still allocates a string[] internally and strings for each line,
                // but avoids the delimiter array allocation per loop iteration and
                // optimises trimming.
                var lines = stackTrace.Split(
                    NewlineDelimiters,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );

                // AddRange is efficient for adding multiple items.
                stackTraceLines.AddRange(lines);
            }
            else
                stackTraceLines.Add("(No stack trace available for this exception level)");

            // Move to the next inner exception
            currentException = currentException.InnerException;
            exceptionLevel++;

            // Add separator (constant string - no allocation)
            if (currentException is not null)
                stackTraceLines.Add("--- Caused by ---");
        }

        stackTraceLines.TrimExcess();

        return stackTraceLines;
    }
}