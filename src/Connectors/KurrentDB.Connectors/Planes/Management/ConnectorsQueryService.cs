// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Connectors.Management.Contracts.Queries;
using FluentValidation;
using Grpc.Core;
using KurrentDB.Connectors.Planes.Management.Domain;
using KurrentDB.Connectors.Planes.Management.Queries;
using Microsoft.Extensions.Logging;
using static KurrentDB.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

namespace KurrentDB.Connectors.Planes.Management;

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
            var result = await runQuery(query, context.CancellationToken).ConfigureAwait(false);

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
