using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Queries;
using FluentValidation;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors.Management;

public class ConnectorsQueryService(
    ILogger<ConnectorsQueryService> logger,
    ConnectorQueries connectorQueries,
    IServiceProvider serviceProvider
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

        var validator = serviceProvider.GetService<IValidator<TQuery>>();

        if (validator is null)
            throw new InvalidOperationException($"No validator found for {query?.GetType().Name}");

        var validationResult = await validator.ValidateAsync(query);

        if (!validationResult.IsValid) {
            logger.LogError("{TraceIdentifier} {CommandType} failed: {ErrorMessage}",
                http.TraceIdentifier,
                query?.GetType().Name,
                validationResult.ToString());

            throw RpcExceptions.InvalidArgument(validationResult);
        }

        try {
            var result = await runQuery(query, context.CancellationToken);

            logger.LogDebug(
                "{TraceIdentifier} {QueryType} executed {Query}",
                http.TraceIdentifier, typeof(TQuery).Name, query
            );

            return result;
        } catch (Exception ex) {
            var rpcEx = ex switch {
                ValidationException vex => RpcExceptions.InvalidArgument(vex.Errors),
                _                       => RpcExceptions.Internal(ex)
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