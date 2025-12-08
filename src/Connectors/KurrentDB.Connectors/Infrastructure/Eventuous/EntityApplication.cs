// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge;

using KurrentDB.Connectors.Planes.Management.Domain;

namespace KurrentDB.Connectors.Infrastructure.Eventuous;

public abstract class EntityApplication<TEntity>(Func<dynamic, string> getEntityId, StreamTemplate streamTemplate, IEventStore store)
    : CommandService<TEntity>(store) where TEntity : State<TEntity>, new() {
    IEventStore Store { get; } = store;

    static readonly string EntityName = typeof(TEntity).Name.Replace("Entity", "").Replace("State", "");

    protected void OnNew<T>(Func<T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(streamTemplate.GetStream(getEntityId(cmd))))
        .ActAsync(async (_, _, cmd, token) => {
            var entityId = getEntityId(cmd);
            var stream   = new StreamName(streamTemplate.GetStream(entityId));

            return await Store.StreamExists(stream, token).Then(exists => exists
                ? throw new DomainExceptions.EntityAlreadyExists(EntityName, entityId)
                : executeCommand(cmd));
        });

    protected void OnExisting<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(streamTemplate.GetStream(getEntityId(cmd))))
        .ActAsync(async (entity, _, cmd, token) => {
            var entityId = getEntityId(cmd);
            var stream   = new StreamName(streamTemplate.GetStream(entityId));

            return await Store.StreamExists(stream, token).Then(exists => !exists
                ? throw new DomainExceptions.EntityNotFound(EntityName, entityId)
                : executeCommand(entity, cmd));
        });

    protected void OnAny<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(streamTemplate.GetStream(getEntityId(cmd))))
        .ActAsync(async (entity, _, cmd, _) => executeCommand(entity, cmd));
}
