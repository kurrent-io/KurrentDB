// ReSharper disable CheckNamespace

using System.Text.Json;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Humanizer;

namespace EventStore.Connectors.Infrastructure;

public static class RpcExceptions {
    public static RpcException Create(StatusCode code, string? message = null) =>
        RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)code,
            Message = !string.IsNullOrWhiteSpace(message) ? $"{message}" : $"{code.Humanize()}"
        });

    public static RpcException Create(StatusCode code, Exception ex) =>
        Create(code, ex.ToString());

    public static RpcException InternalServerError(Exception exception) =>
        RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)StatusCode.Internal,
            Message = "Internal Server Error",
            Details = { Any.Pack(exception.ToRpcDebugInfo()) }
        });

    public static RpcException PreconditionFailure(List<ValidationFailure> failures) =>
        RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)StatusCode.FailedPrecondition,
            Message = "Precondition Failure",
            Details = { Any.Pack(new PreconditionFailure {
                Violations = { failures.Select(failure => new PreconditionFailure.Types.Violation {
                    Subject     = failure.PropertyName,
                    Description = failure.ErrorMessage
                })}
            })}
        });

    public static RpcException BadRequest(List<ValidationFailure> failures) =>
        // TODO JC: Just stringify and put the validation errors in the message.
        // Because of https://github.com/dotnet/aspnetcore/pull/51394.
        RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)StatusCode.InvalidArgument,
            Message = $"Bad Request - {JsonSerializer.Serialize(failures)}",
            Details = { Any.Pack(new BadRequest {
                FieldViolations = { failures.Select(failure => new BadRequest.Types.FieldViolation {
                    Field       = failure.PropertyName,
                    Description = failure.ErrorMessage
                })}
            })}
        });

    public static RpcException BadRequest(IDictionary<string, string[]> failures) =>
        // TODO JC: Just stringify and put the validation errors in the message.
        // Because of https://github.com/dotnet/aspnetcore/pull/51394.
        RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)StatusCode.InvalidArgument,
            Message = $"Bad Request - {JsonSerializer.Serialize(failures)}",
            Details = { Any.Pack(new BadRequest {
                FieldViolations = { failures.Select(failure => new BadRequest.Types.FieldViolation {
                    Field       = failure.Key,
                    Description = failure.Value.Aggregate((a, b) => $"{a}, {b}")
                })}
            })}
        });
}