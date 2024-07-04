using System.Text.Json;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Plugins.Authorization;
using Eventuous;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using static EventStore.Connectors.Management.Contracts.Commands.ConnectorCommandService;
using ErrorHandleResult = Eventuous.ErrorResult<EventStore.Connectors.Management.ConnectorEntity>;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors.Management;

/// <summary>
///  Connector service implementation.
/// </summary>
public class ConnectorService(ConnectorApplication application, IAuthorizationProvider authorizationProvider) : ConnectorCommandServiceBase {
    public override Task<Empty> Create(CreateConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Reconfigure(ReconfigureConnector request, ServerCallContext context) => Execute(request, context);
    public override Task<Empty> Delete(DeleteConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Start(StartConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Stop(StopConnector request, ServerCallContext context)               => Execute(request, context);
    public override Task<Empty> Reset(ResetConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Rename(RenameConnector request, ServerCallContext context)           => Execute(request, context);

    // Constraints for code in the below method due to the fact that EventStore exposes Google.Rpc.Status and Google.Rpc.Code:
    // 1. We are forced to use the static invocation of RpcStatusExtensions.ToRpcException to
    //    avoid explicitly referencing Google.Rpc.Status (leading to unresolvable ambiguous reference).
    // TODO JC: Can we stop EventStore exposing these types explicitly?
    async Task<Empty> Execute<TCommand>(TCommand command, ServerCallContext context) where TCommand : class {
        var user = context.GetHttpContext().User;

        var authorized = await authorizationProvider.CheckAccessAsync(
            user, new Operation("connectors", "write"), context.CancellationToken
        );

        if (!authorized) {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "You do not have permission to perform this operation."
            ));
        }

        var handleResult = await application.Handle(command, context.CancellationToken);

        if (handleResult is not ErrorHandleResult errorHandleResult)
            return new Empty();

        throw errorHandleResult.Exception switch {
            // TODO JC: Just stringify and put the validation errors in the message.
            // Because of https://github.com/dotnet/aspnetcore/pull/51394.
            ConnectorDomainExceptions.InvalidConnectorSettings invalidConnectorSettings =>
                RpcStatusExtensions.ToRpcException(
                    new() {
                        Code    = (int)StatusCode.InvalidArgument,
                        Message = $"Invalid connector settings: {JsonSerializer.Serialize(invalidConnectorSettings.Errors)}",
                        Details = {
                            // TODO JC: Reimplement when above is fixed (.NET 9)
                            // Any.Pack(
                            //     new BadRequest {
                            //         FieldViolations = {
                            //             invalidConnectorSettings.Errors.SelectMany(
                            //                     kvp => kvp.Value.Select(
                            //                         error => new BadRequest.Types.FieldViolation {
                            //                             Field       = kvp.Key,
                            //                             Description = error
                            //                         }
                            //                     )
                            //                 )
                            //                 .ToList()
                            //         }
                            //     }
                            // )
                        }
                    }
                ),

            DomainException domainException => new RpcException(new Status(StatusCode.FailedPrecondition, domainException.Message)),

            _ => new RpcException(new Status(StatusCode.Internal, errorHandleResult.Exception!.Message))
        };
    }
}