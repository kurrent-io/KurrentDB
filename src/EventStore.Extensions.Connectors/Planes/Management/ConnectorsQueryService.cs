using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Queries;
using FluentValidation;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

namespace EventStore.Connectors.Management;

public class ConnectorsQueryService(
    ConnectorQueries connectorQueries,
    RequestValidationService requestValidationService,
    ILogger<ConnectorsQueryService> logger
) : ConnectorsQueryServiceBase {
    public override  Task<ListConnectorsResult> List(ListConnectors query, ServerCallContext context) =>
         Execute(connectorQueries.List, query, context);

    public override Task<GetConnectorSettingsResult> GetSettings(GetConnectorSettings query, ServerCallContext context) =>
         Execute(connectorQueries.GetSettings, query, context);

    async Task<TQueryResult> Execute<TQuery, TQueryResult>(RunQuery<TQuery, TQueryResult> runQuery, TQuery query, ServerCallContext context) {
        var http = context.GetHttpContext();

        var authenticated = http.User.Identity?.IsAuthenticated ?? false;
        if (!authenticated)
            throw RpcExceptions.PermissionDenied();

        var validationResult = requestValidationService.Validate(query);
        if (!validationResult.IsValid)
            throw RpcExceptions.InvalidArgument(validationResult);

        try {
            var result = await runQuery(query, context.CancellationToken);

            logger.LogDebug(
                "{TraceIdentifier} {QueryType} executed {Query}",
                http.TraceIdentifier, typeof(TQuery).Name, query
            );

            return result;
        } catch (Exception ex) {
            var rpcEx = ex switch {
                ValidationException vex              => RpcExceptions.InvalidArgument(vex.Errors),
                DomainExceptions.EntityNotFound vfex => RpcExceptions.NotFound(vfex),
                _                                    => RpcExceptions.Internal(ex)
            };

            logger.LogError(ex,
                "{TraceIdentifier} {QueryType} failed {Query}",
                http.TraceIdentifier,
                typeof(TQuery).Name,
                query);

            throw rpcEx;
        }
    }

    delegate Task<TQueryResult> RunQuery<in TQuery, TQueryResult>(TQuery query, CancellationToken cancellationToken);
}