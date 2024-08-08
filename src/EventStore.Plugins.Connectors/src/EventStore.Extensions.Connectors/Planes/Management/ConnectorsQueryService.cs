using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Queries;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors.Management;

public class ConnectorsQueryService(
    ILogger<ConnectorsQueryService> logger,
    ConnectorQueries connectorQueries
) : ConnectorsQueryServiceBase {
    public override async Task<ListConnectorsResult> ListConnectors(ListConnectorsQuery query, ServerCallContext ctx)
        => await Execute(connectorQueries.ListConnectors, query, ctx);

    // public override async Task<ImaginaryQueryResult> ImaginaryQuery(ImaginaryQuery query, ServerCallContext ctx)
    //     => await Execute(connectorQueries.ImaginaryQuery, query, ctx);

    async Task<TQueryResult> Execute<TQuery, TQueryResult>(
        GetQueryResult<TQuery, TQueryResult> getQueryResult,
        TQuery query,
        ServerCallContext ctx
    ) {
        try {
            logger.LogDebug("{TraceIdentifier} Executed {QueryType} {Query}",
                ctx.GetHttpContext().TraceIdentifier,
                typeof(TQuery).Name,
                query);

            return await getQueryResult(query, ctx.CancellationToken);
        } catch (Exception ex) {
            var rpcEx = ex switch {
                InvalidConnectorQueryException icq => RpcExceptions.BadRequest(icq.Errors),
                _                                  => RpcExceptions.InternalServerError(ex)
            };

            logger.LogError(ex,
                "{TraceIdentifier} Failed {QueryType} {Query}",
                ctx.GetHttpContext().TraceIdentifier,
                typeof(TQuery).Name,
                query);

            throw rpcEx;
        }
    }

    delegate Task<TQueryResult> GetQueryResult<in TQuery, TQueryResult>(
        TQuery query,
        CancellationToken cancellationToken
    );
}