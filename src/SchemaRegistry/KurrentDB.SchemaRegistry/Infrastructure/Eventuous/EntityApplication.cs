// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Services.Domain;

namespace KurrentDB.SchemaRegistry.Infrastructure.Eventuous;

public abstract class EntityApplication<TEntity>(IEventStore store)
    : CommandService<TEntity>(store) where TEntity : State<TEntity>, new() {
    IEventStore Store { get; } = store;

    static readonly string EntityName = typeof(TEntity).Name.Replace("Entity", "").Replace("State", "");

    protected abstract StreamTemplate        StreamTemplate { get; }

    protected void OnExisting<T>(Func<TEntity, T, IEnumerable<object>> executeCommand)
	    where T : class, IEntityCommand => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(cmd.EntityId)))
        .ActAsync(async (entity, _, cmd, ct) => {
            var entityId = cmd.EntityId;
            var stream   = new StreamName(StreamTemplate.GetStream(entityId));
            return await Store.StreamExists(stream, ct).Then(
                exists => !exists
                    ? throw new DomainExceptions.EntityNotFound(EntityName, entityId)
                    : executeCommand(entity, cmd)
            );
        });

    protected void OnExisting<T>(Func<TEntity, T, CancellationToken, ValueTask<IEnumerable<object>>> executeCommand)
	    where T : class, IEntityCommand => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(cmd.EntityId)))
        .ActAsync(async (entity, _, cmd, ct) => {
            var entityId = cmd.EntityId;
            var stream   = new StreamName(StreamTemplate.GetStream(entityId));
            var exists   = await Store.StreamExists(stream, ct);

            if (!exists)
                throw new DomainExceptions.EntityNotFound(EntityName, entityId);

            return await executeCommand(entity, cmd, ct);
        });

    protected void OnAny<T>(Func<TEntity, T, IEnumerable<object>> executeCommand)
	    where T : class, IEntityCommand => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(cmd.EntityId)))
        .ActAsync((entity, _, cmd, _) => Task.FromResult(executeCommand(entity, cmd)));
}
