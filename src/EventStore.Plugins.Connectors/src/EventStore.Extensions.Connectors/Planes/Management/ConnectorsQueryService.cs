using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Queries;
using FluentValidation;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors.Management;

public class ConnectorsQueryService(ILogger<ConnectorsQueryService> logger, ConnectorQueries connectorQueries) : ConnectorsQueryServiceBase {
    public override  Task<ListConnectorsResult> List(ListConnectors query, ServerCallContext context) =>
         Execute(connectorQueries.List, query, context);

    public override Task<GetConnectorSettingsResult> GetSettings(GetConnectorSettings query, ServerCallContext context) =>
         Execute(connectorQueries.GetSettings, query, context);

    async Task<TQueryResult> Execute<TQuery, TQueryResult>(RunQuery<TQuery, TQueryResult> runQuery, TQuery query, ServerCallContext context) {
        var http = context.GetHttpContext();

        var authenticated = http.User.Identity?.IsAuthenticated ?? false;
        if (!authenticated)
            throw RpcExceptions.Create(StatusCode.PermissionDenied);

        try {
            var result = await runQuery(query, context.CancellationToken);

            logger.LogDebug(
                "{TraceIdentifier} {QueryType} executed {Query}",
                http.TraceIdentifier, typeof(TQuery).Name, query
            );

            return result;
        } catch (Exception ex) {
            var rpcEx = ex switch {
                ValidationException vex => RpcExceptions
                    .BadRequest(vex.Errors.GroupBy(x => x.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())),
                _ => RpcExceptions.InternalServerError(ex)
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