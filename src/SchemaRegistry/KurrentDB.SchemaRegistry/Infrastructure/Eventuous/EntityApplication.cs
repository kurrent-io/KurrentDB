// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.SchemaRegistry.Services.Domain;

namespace KurrentDB.SchemaRegistry.Infrastructure.Eventuous;

public abstract class EntityApplication<TEntity>(IEventStore store)
    : CommandService<TEntity>(store) where TEntity : State<TEntity>, new() {
    IEventStore Store { get; } = store;

    static readonly string EntityName = typeof(TEntity).Name.Replace("Entity", "").Replace("State", "");

    protected abstract Func<dynamic, string> GetEntityId    { get; }
    protected abstract StreamTemplate        StreamTemplate { get; }

    protected void OnNew<T>(Func<T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(GetEntityId(cmd))))
        .ActAsync(async (_, _, cmd, token) => {
            var entityId = GetEntityId(cmd);
            var stream   = new StreamName(StreamTemplate.GetStream(entityId));
            return await Store.StreamExists(stream, token).Then(
                exists => exists
                    ? throw new DomainExceptions.EntityAlreadyExists(EntityName, entityId)
                    : executeCommand(cmd)
            );
        });

    protected void OnExisting<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(GetEntityId(cmd))))
        .ActAsync(async (entity, _, cmd, ct) => {
            var entityId = GetEntityId(cmd);
            var stream   = new StreamName(StreamTemplate.GetStream(entityId));
            return await Store.StreamExists(stream, ct).Then(
                exists => !exists
                    ? throw new DomainExceptions.EntityNotFound(EntityName, entityId)
                    : executeCommand(entity, cmd)
            );
        });

    protected void OnExisting<T>(Func<dynamic, CancellationToken, ValueTask<string>> getEntityId, Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStreamAsync(async (cmd, ct) => {
            var id = await getEntityId(cmd, ct);
            return new(StreamTemplate.GetStream(id));
        })
        .ActAsync(async (entity, _, cmd, ct) => {
            var entityId = await getEntityId(cmd, ct);
            var stream   = new StreamName(StreamTemplate.GetStream(entityId));
            return await Store.StreamExists(stream, ct).Then(
                exists => !exists
                    ? throw new DomainExceptions.EntityNotFound(EntityName, entityId)
                    : executeCommand(entity, cmd)
            );
        });

    protected void OnAny<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(GetEntityId(cmd))))
        .ActAsync((entity, _, cmd, _) => Task.FromResult(executeCommand(entity, cmd)));

    protected void OnAny<T>(Func<TEntity, T, CancellationToken, ValueTask<IEnumerable<object>>> executeCommand) where T : class => On<T>()
        .InState(ExpectedState.Any)
        .GetStream(cmd => new(StreamTemplate.GetStream(GetEntityId(cmd))))
        .ActAsync(async (entity, _, cmd, ct) => await executeCommand(entity, cmd, ct));


}