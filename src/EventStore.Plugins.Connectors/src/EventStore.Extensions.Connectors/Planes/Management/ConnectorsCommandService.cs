// ReSharper disable once CheckNamespace

using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Streaming;
using Eventuous;
using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.Contracts.Commands.ConnectorsCommandService;

namespace EventStore.Connectors.Management;

public class ConnectorsCommandService(
    ConnectorsCommandApplication application,
    RequestValidationService requestValidationService,
    ILogger<ConnectorsCommandService> logger
) : ConnectorsCommandServiceBase {
    public override Task<Empty> Create(CreateConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Reconfigure(ReconfigureConnector request, ServerCallContext context) => Execute(request, context);
    public override Task<Empty> Delete(DeleteConnector request, ServerCallContext context)           => Execute(request, context);
    public override Task<Empty> Start(StartConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Reset(ResetConnector request, ServerCallContext context)             => Execute(request, context);
    public override Task<Empty> Stop(StopConnector request, ServerCallContext context)               => Execute(request, context);
    public override Task<Empty> Rename(RenameConnector request, ServerCallContext context)           => Execute(request, context);

    async Task<Empty> Execute<TCommand>(TCommand command, ServerCallContext context) where TCommand : class {
        var http = context.GetHttpContext();

        var authenticated = http.User.Identity?.IsAuthenticated ?? false;
        if (!authenticated)
            throw RpcExceptions.PermissionDenied();

        var validationResult = requestValidationService.Validate(command);
        if (!validationResult.IsValid)
            throw RpcExceptions.InvalidArgument(validationResult);

        var result = await application.Handle(command, context.CancellationToken);

        return result.Match(
            _ => {
                logger.LogDebug(
                    "{TraceIdentifier} {CommandType} executed [connector_id, {ConnectorId}]",
                    http.TraceIdentifier, command.GetType().Name, http.Request.RouteValues.First().Value
                );

                return new Empty();
            },
            error => {
                // TODO SS: BadRequest should be agnostic, but dont know how to handle this yet, perhaps check for some specific ex type later on...
                // TODO SS: improve this exception mess later (we dont control the command service from eventuous)

                var rpcEx = error.Exception switch {
                    ValidationException ex                  => RpcExceptions.InvalidArgument(ex.Errors),
                    InvalidConnectorSettingsException ex    => RpcExceptions.InvalidArgument(ex.Errors),
                    DomainExceptions.EntityAlreadyExists ex => RpcExceptions.AlreadyExists(ex),
                    DomainExceptions.EntityDeleted ex       => RpcExceptions.NotFound(ex),
                    StreamAccessDeniedError ex              => RpcExceptions.PermissionDenied(ex),
                    StreamNotFoundError ex                  => RpcExceptions.NotFound(ex),
                    StreamDeletedError ex                   => RpcExceptions.FailedPrecondition(ex),
                    ExpectedStreamRevisionError ex          => RpcExceptions.FailedPrecondition(ex),
                    DomainException ex                      => RpcExceptions.FailedPrecondition(ex),
                    InvalidOperationException ex            => RpcExceptions.InvalidArgument(ex),

                    // Eventuous framework error and I think we can remove it but need moar tests...
                    // StreamNotFound ex => RpcExceptions.Create(StatusCode.NotFound, ex.Message),

                    { } ex => RpcExceptions.Internal(ex)
                };

                if (rpcEx.StatusCode == StatusCode.Internal)
                    logger.LogError(error.Exception,
                        "{TraceIdentifier} {CommandType} failed",
                        http.TraceIdentifier, command.GetType().Name);
                else
                    logger.LogError(
                        "{TraceIdentifier} {CommandType} failed: {ErrorMessage}",
                        http.TraceIdentifier, command.GetType().Name, error.Exception.Message);

                throw rpcEx;
            }
        );
    }
}