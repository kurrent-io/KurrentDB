using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Plugins.Authorization;
using Eventuous;
using Grpc.Core;
using OkHandleResult = Eventuous.OkResult<EventStore.Connectors.Management.ConnectorEntity>;
using ErrorHandleResult = Eventuous.ErrorResult<EventStore.Connectors.Management.ConnectorEntity>;

namespace EventStore.Connectors.Management;

/// <summary>
///  Connector service implementation.
/// </summary>
public class ConnectorService(ConnectorApplication application, IAuthorizationProvider authorizationProvider)
    : ConnectorCommandService.ConnectorCommandServiceBase {
    ConnectorApplication Application { get; } = application;

    public override Task<CommandResult> Create(CreateConnector request, ServerCallContext context) =>
        Execute(request, context);

    public override Task<CommandResult> Reconfigure(ReconfigureConnector request, ServerCallContext context) =>
        Execute(request, context);

    public override Task<CommandResult> Delete(DeleteConnector request, ServerCallContext context) =>
        Execute(request, context);

    public override Task<CommandResult> Start(StartConnector request, ServerCallContext context) =>
        Execute(request, context);

    public override Task<CommandResult> Stop(StopConnector request, ServerCallContext context) =>
        Execute(request, context);

    public override Task<CommandResult> Reset(ResetConnector request, ServerCallContext context) =>
        Execute(request, context);

    public override Task<CommandResult> Rename(RenameConnector request, ServerCallContext context) =>
        Execute(request, context);

    async Task<CommandResult> Execute<TCommand>(TCommand command, ServerCallContext context) where TCommand : class {
        var user      = context.GetHttpContext().User;
        var requestId = context.GetHttpContext().TraceIdentifier;

        await authorizationProvider.CheckAccessAsync(
            user,
            new Operation("connectors", "write"),
            context.CancellationToken
        );

        var handleResult = await Application.Handle(command, context.CancellationToken);

        // TODO JC: Need to tidy this up and break it down into smaller methods.
        return handleResult switch {
            OkHandleResult
                => new CommandResult {
                    RequestId = requestId,
                    Status    = new RpcStatus { Code = RpcStatusCode.Ok }
                },
            ErrorHandleResult {
                    Exception: ConnectorDomainExceptions.InvalidConnectorSettings invalidConnectorSettings
                } errorResult
                => new CommandResult {
                    RequestId = requestId,
                    ValidationProblem = new ValidationProblem {
                        Failures = {
                            invalidConnectorSettings.Errors.Select(
                                err => new ValidationFailure {
                                    PropertyName = err.Key,
                                    Errors       = { err.Value }
                                }
                            )
                        }
                    },
                    Status = new RpcStatus {
                        Code    = RpcStatusCode.FailedPrecondition,
                        Message = errorResult.Exception!.Message,
                    }
                },
            ErrorHandleResult { Exception: DomainException } errorResult
                => new CommandResult {
                    RequestId = requestId,
                    Status = new RpcStatus {
                        Code    = RpcStatusCode.FailedPrecondition,
                        Message = errorResult.Exception!.Message
                    }
                },
            ErrorHandleResult { Exception: not DomainException } errorResult
                => new CommandResult {
                    RequestId = requestId,
                    Status = new RpcStatus {
                        Code    = RpcStatusCode.Internal,
                        Message = errorResult.Exception?.Message
                    }
                },
            _
                => new CommandResult {
                    RequestId = requestId,
                    Status = new RpcStatus {
                        Code    = RpcStatusCode.Unknown,
                        Message = "Unknown error"
                    }
                }
        };
    }
}