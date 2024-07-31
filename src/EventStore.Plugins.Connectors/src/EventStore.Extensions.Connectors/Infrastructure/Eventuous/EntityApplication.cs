using EventStore.Connectors.Management;
using EventStore.Streaming;
using Eventuous;

namespace EventStore.Connectors.Eventuous;

public abstract class EntityApplication<TEntity>(Func<dynamic, string> getEntityId, StreamTemplate streamTemplate, IEventStore store)
    : CommandService<TEntity>(store) where TEntity : State<TEntity>, new() {
    IEventStore Store { get; } = store;

    protected void OnNew<T>(Func<T, IEnumerable<object>> executeCommand) where T : class =>
        On<T>()
            .InState(ExpectedState.New)
            .GetStream(cmd => new(streamTemplate.GetStream(getEntityId(cmd))))
            .ActAsync(async (cmd, token) => {
                var entityName = typeof(TEntity).Name.Replace("Entity", "").Replace("State", "");
                var entityId   = getEntityId(cmd);
                var stream     = new StreamName(streamTemplate.GetStream(entityId));

                await Store.StreamExists(stream, token).Then(exists => exists
                    ? throw new DomainExceptions.EntityAlreadyExists(entityName, entityId)
                    : Task.CompletedTask
                );

                return executeCommand(cmd);
            });

    protected void OnExisting<T>(Func<TEntity, T, IEnumerable<object>> executeCommand) where T : class =>
        On<T>()
            .InState(ExpectedState.Existing)
            .GetStream(cmd => new(streamTemplate.GetStream(getEntityId(cmd))))
            .Act((entity, _, cmd) => executeCommand(entity, cmd));
}