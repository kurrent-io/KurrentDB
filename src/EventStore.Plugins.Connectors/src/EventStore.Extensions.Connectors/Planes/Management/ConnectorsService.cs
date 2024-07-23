using System.Text.Json;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Plugins.Authorization;
using EventStore.Streaming;
using Eventuous;
using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Humanizer;

using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.Contracts.Commands.ConnectorsService;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors.Management;

public class ConnectorsService(ConnectorApplication application, IAuthorizationProvider authorizationProvider) : ConnectorsServiceBase {
    public override Task<Empty> Create(CreateConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Reconfigure(ReconfigureConnector request, ServerCallContext context) => Execute(request, context);
    public override Task<Empty> Delete(DeleteConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Start(StartConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Stop(StopConnector request, ServerCallContext context)               => Execute(request, context);
    public override Task<Empty> Reset(ResetConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Rename(RenameConnector request, ServerCallContext context)           => Execute(request, context);

    async Task<Empty> Execute<TCommand>(TCommand command, ServerCallContext context) where TCommand : class {
        // TODO SS: should we check for auth first?
        // TODO SS: when can operations be cancelled? what is the best way to handle this? Is it automatic?

        var authorized = await authorizationProvider.CheckAccessAsync(
            context.GetHttpContext().User,
            new Operation("connectors", "write"), // TODO SS: what claim must we check? where are they written too? are they hardcoded? license entitlements?
            context.CancellationToken
        );

        if (!authorized)
            throw RpcExceptions.Create(StatusCode.PermissionDenied);

        var commandResult = await application.Handle(command, context.CancellationToken);

        if (commandResult is not ErrorResult<ConnectorEntity> errorResult)
            return new Empty();

        // TODO SS: InvalidArgument/BadRequest should be agnostic, but dont know how to handle this yet, perhaps check for some specific ex type later on...

        throw errorResult.Exception switch {
            // TODO JC: Just stringify and put the validation errors in the message.
            // Because of https://github.com/dotnet/aspnetcore/pull/51394.
            InvalidConnectorSettingsException ex => RpcExceptions.Create(StatusCode.InvalidArgument, $"{ex.Message}: {JsonSerializer.Serialize(ex.Errors)}"),
            StreamNotFoundError ex               => RpcExceptions.Create(StatusCode.NotFound, ex),
            StreamDeletedError ex                => RpcExceptions.Create(StatusCode.FailedPrecondition, ex),
            StreamAccessDeniedError ex           => RpcExceptions.Create(StatusCode.PermissionDenied, ex),
            ExpectedStreamRevisionError ex       => RpcExceptions.Create(StatusCode.FailedPrecondition, ex),
            DomainException ex                   => RpcExceptions.Create(StatusCode.FailedPrecondition, ex),
            StreamNotFound ex                    => RpcExceptions.Create(StatusCode.NotFound, ex), // eventuous error and I think I can remove it.

            { } err => RpcExceptions.Create(StatusCode.Internal, err)
        };
    }
}

static class RpcExceptions {
    public static RpcException Create(StatusCode code, string? message = null) =>
        RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)code,
            Message = !string.IsNullOrWhiteSpace(message) ? $"{message}" : $"{code.Humanize()}"
        });

    public static RpcException Create(StatusCode code, Exception ex) => Create(code, ex.Message);

    public static RpcException BadRequest(string message, List<ValidationFailure> failures) {
        // TODO JC: Just stringify and put the validation errors in the message.
        // Because of https://github.com/dotnet/aspnetcore/pull/51394.

        return RpcStatusExtensions.ToRpcException(new() {
            Code    = (int)StatusCode.InvalidArgument,
            Message = $"{message}: {JsonSerializer.Serialize(failures)}",
        });

        // Details = {
        //     // TODO JC: Reimplement when above is fixed (.NET 9)
        //     // Any.Pack(
        //     //     new BadRequest {
        //     //         FieldViolations = {
        //     //             invalidConnectorSettings.Errors.SelectMany(
        //     //                     kvp => kvp.Value.Select(
        //     //                         error => new BadRequest.Types.FieldViolation {
        //     //                             Field       = kvp.Key,
        //     //                             Description = error
        //     //                         }
        //     //                     )
        //     //                 )
        //     //                 .ToList()
        //     //         }
        //     //     }
        //     // )
        // }
    }
}