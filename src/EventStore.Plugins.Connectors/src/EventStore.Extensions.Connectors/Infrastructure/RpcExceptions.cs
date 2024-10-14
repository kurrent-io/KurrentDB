// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System.Text.Json;
using FluentValidation.Results;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

namespace EventStore.Connectors.Infrastructure;

public static class RpcExceptions {
    static RpcException Create(StatusCode statusCode, string message, IMessage? detail = null) {
        if (detail is not null) {
            throw RpcStatusExtensions.ToRpcException(new() {
                Code    = (int)statusCode,
                Message = message,
                Details = { Any.Pack(detail) }
            });
        }

        throw RpcStatusExtensions.ToRpcException(new() {
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
                new MapField<string, string> {
                    failures.ToDictionary(failure => failure.Key,
                        failure => string.Join(", ", failure.Value))
                }
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
            StackEntries = { ex.StackTrace?.Split(Environment.NewLine) ?? Enumerable.Empty<string>() }
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