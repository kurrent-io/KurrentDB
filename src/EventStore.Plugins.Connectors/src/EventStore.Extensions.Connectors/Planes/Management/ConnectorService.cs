using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Plugins.Authorization;
using Eventuous;
using Grpc.Core;

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

    public override Task<CommandResult>
        RecordStateChange(RecordConnectorStateChange request, ServerCallContext context) => Execute(request, context);

    public override Task<CommandResult> RecordPositions(RecordConnectorPositions request, ServerCallContext context) =>
        Execute(request, context);

    async Task<CommandResult> Execute<TCommand>(TCommand command, ServerCallContext context) where TCommand : class {
        var user      = context.GetHttpContext().User;
        var requestId = context.GetHttpContext().TraceIdentifier;

        await authorizationProvider.CheckAccessAsync(
            user,
            new Operation("connectors", "write"),
            context.CancellationToken
        );

        try {
            await Application.Handle(command, context.CancellationToken);

            return new CommandResult {
                RequestId = requestId,
                Status    = new RpcStatus { Code = RpcStatusCode.Ok }
            };
        } catch (DomainException dex) {
            return new CommandResult {
                RequestId = requestId,
                Status = new RpcStatus {
                    Code    = RpcStatusCode.FailedPrecondition,
                    Message = dex.Message
                }
            };
        } catch (Exception ex) {
            return new CommandResult {
                RequestId = requestId,
                Status = new RpcStatus {
                    Code    = RpcStatusCode.Internal,
                    Message = ex.Message
                },
            };
        }
    }
}