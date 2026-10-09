// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge;
using KurrentDB.Connectors.Management.Contracts.Commands;
using KurrentDB.Connectors.Planes.Management.Domain;

namespace KurrentDB.Connectors.Infrastructure.Eventuous;

public abstract class EntityApplication<TEntity>(StreamTemplate streamTemplate, IEventStore store)
    : CommandService<TEntity>(store) where TEntity : State<TEntity>, new() {
    IEventStore Store { get; } = store;

    static readonly string EntityName = typeof(TEntity).Name.Replace("Entity", "").Replace("State", "");

    protected void OnExisting<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class, ICommand => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(streamTemplate.GetStream(cmd.EntityId)))
        .ActAsync(async (entity, _, cmd, token) => {
	        var entityId = cmd.EntityId;
            var stream   = new StreamName(streamTemplate.GetStream(entityId));

            return await Store.StreamExists(stream, token).Then(exists => !exists
                ? throw new DomainExceptions.EntityNotFound(EntityName, entityId)
                : executeCommand(entity, cmd));
        });

    protected void OnAny<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class, ICommand => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(streamTemplate.GetStream(cmd.EntityId)))
        .ActAsync(async (entity, _, cmd, _) => executeCommand(entity, cmd));
}
