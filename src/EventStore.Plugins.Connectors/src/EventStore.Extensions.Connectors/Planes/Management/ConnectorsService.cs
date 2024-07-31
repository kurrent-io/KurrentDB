using System.Text.Json;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Streaming;
using Eventuous;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Humanizer;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.Contracts.Commands.ConnectorsService;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors.Management;

public class ConnectorsService(ConnectorsApplication application, ILogger<ConnectorsService> logger) : ConnectorsServiceBase {
    public override Task<Empty> Create(CreateConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Reconfigure(ReconfigureConnector request, ServerCallContext context) => Execute(request, context);
    public override Task<Empty> Delete(DeleteConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Start(StartConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Stop(StopConnector request, ServerCallContext context)               => Execute(request, context);
    public override Task<Empty> Reset(ResetConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Rename(RenameConnector request, ServerCallContext context)           => Execute(request, context);

    async Task<Empty> Execute<TCommand>(TCommand command, ServerCallContext context) where TCommand : class {
        var authenticated = context.GetHttpContext().User.Identity?.IsAuthenticated ?? false;
        if (!authenticated)
            throw RpcExceptions.Create(StatusCode.PermissionDenied);

        //logger.LogInformation("Executing {CommandType} {Command}", command.GetType().Name, command);
        var commandResult = await application.Handle(command, context.CancellationToken);

        if (commandResult is not ErrorResult<ConnectorEntity> errorResult) {
            // SHOULD NOT THROW WITH STUPID UNREGISTERED EVENT TYPE IN EVENTUOUS. IT ALREADY HAS THE OBJECT FFS!
            // foreach (var change in commandResult.Changes.EmptyIfNull())
            //     logger.LogInformation("{@Event}", change.Event);

            logger.LogInformation(
                "{TraceIdentifier} Executed {CommandType} {Command}",
                context.GetHttpContext().TraceIdentifier, command.GetType().Name, command
            );

            return new Empty();
        }

        logger.LogError(
            errorResult.Exception,
            "{TraceIdentifier} Executed {CommandType} {Command}",
            context.GetHttpContext().TraceIdentifier, command.GetType().Name, command
        );

        // TODO SS: BadRequest should be agnostic, but dont know how to handle this yet, perhaps check for some specific ex type later on...

        throw errorResult.Exception switch {
            InvalidConnectorSettingsException ex => RpcExceptions.BadRequest(ex.Errors),
            StreamAccessDeniedError ex           => RpcExceptions.Create(StatusCode.PermissionDenied, ex),
            StreamNotFoundError ex               => RpcExceptions.Create(StatusCode.NotFound, ex),
            StreamDeletedError ex                => RpcExceptions.Create(StatusCode.FailedPrecondition, ex),
            ExpectedStreamRevisionError ex       => RpcExceptions.Create(StatusCode.FailedPrecondition, ex),
            DomainException ex                   => RpcExceptions.Create(StatusCode.FailedPrecondition, ex),

            // Eventuous framework error and I think I can remove it.
            StreamNotFound ex => RpcExceptions.Create(StatusCode.NotFound, ex),

            { } ex => RpcExceptions.InternalServerError(ex)
        };
    }
}

static class RpcExceptions {
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