// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable once CheckNamespace

using KurrentDB.Connectors.Management.Contracts.Commands;
using Kurrent.Surge;
using Eventuous;
using FluentValidation;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using KurrentDB.Connectors.Management.Contracts.Queries;
using KurrentDB.Connectors.Planes.Management;
using KurrentDB.Connectors.Planes.Management.Domain;
using KurrentDB.Connectors.Planes.Management.Queries;
using Microsoft.Extensions.Logging;
using static System.StringComparison;
using static KurrentDB.Connectors.Planes.Management.Domain.ConnectorDomainExceptions;
using static KurrentDB.Connectors.Management.Contracts.Commands.ConnectorsCommandService;

namespace KurrentDB.Connectors.Management;

public class ConnectorsCommandService(
    ConnectorsCommandApplication application,
    ConnectorQueries connectorQueries,
    RequestValidationService requestValidationService,
    ILogger<ConnectorsCommandService> logger
) : ConnectorsCommandServiceBase {
    public override async Task<Empty> Create(CreateConnector request, ServerCallContext context) {
        var instanceType = request.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

    public override async Task<Empty> Reconfigure(ReconfigureConnector request, ServerCallContext context) {
        var instanceType = request.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

    public override async Task<Empty> Delete(DeleteConnector request, ServerCallContext context) {
        var connector = await connectorQueries.GetSettings(new GetConnectorSettings { ConnectorId = request.ConnectorId }, context.CancellationToken);

        var instanceType = connector.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

    public override async Task<Empty> Start(StartConnector request, ServerCallContext context) {
        var connector = await connectorQueries.GetSettings(new GetConnectorSettings { ConnectorId = request.ConnectorId }, context.CancellationToken);

        var instanceType = connector.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

    public override async Task<Empty> Stop(StopConnector request, ServerCallContext context) {
        var connector = await connectorQueries.GetSettings(new GetConnectorSettings { ConnectorId = request.ConnectorId }, context.CancellationToken);

        var instanceType = connector.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

    public override async Task<Empty> Reset(ResetConnector request, ServerCallContext context) {
        var connector = await connectorQueries.GetSettings(new GetConnectorSettings { ConnectorId = request.ConnectorId }, context.CancellationToken);

        var instanceType = connector.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

    public override async Task<Empty> Rename(RenameConnector request, ServerCallContext context) {
        var connector = await connectorQueries.GetSettings(new GetConnectorSettings { ConnectorId = request.ConnectorId }, context.CancellationToken);

        var instanceType = connector.Settings
            .FirstOrDefault(kvp => kvp.Key.Equals(nameof(IConnectorOptions.InstanceTypeName), OrdinalIgnoreCase))
            .Value;

        if (ConnectorCatalogue.TryGetConnector(instanceType, out var item) && item.AllowsMultipleInstances) {
            await Task.WhenAll(request
                .Repeat(5)
                .Skip(1)
                .Select(r => Execute(r, context))
                .ToList());
        }

        return await Execute(request, context);
    }

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

public static class ConnectorsCommandExtensions {
    public static List<T> Repeat<T>(this T message, int count) where T : IMessage<T> {
        var property    = typeof(T).GetProperty(nameof(ConnectorId));
        var connectorId = (property!.GetValue(message) as string)!;

        return Enumerable.Range(1, count)
            .Select(i => {
                var clone = message.Clone();
                property.SetValue(clone, i == 1 ? connectorId : $"{connectorId}-{i - 1}");
                return clone;
            })
            .ToList();
    }
}
