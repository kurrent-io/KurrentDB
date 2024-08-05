using EventStore.Connectors.Management;
using EventStore.Streaming;
using Eventuous;

namespace EventStore.Connectors.Eventuous;

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
}

// public abstract class SmartCommandService<TState>
//     : ICommandService<TState> where TState : State<TState>, new() {
//     public SmartCommandService(IEventStore store) {
//         Router = new(MessageBroadcasterStrategy.Sequential);
//     }
//
//     MessageRouter Router { get; }
//
//     protected void On<T>(Func<T, InterceptorContext, Task> handler) where T : InterceptorEvent =>
//         Router.RegisterHandler<T, InterceptorContext>(
//             async (evt, ctx) => {
//                 using var scope = ctx.Logger.BeginPropertyScope(LogProperties);
//                 try {
//                     await handler(evt, ctx);
//                 }
//                 catch (Exception ex) {
//                     ctx.Logger.LogWarning(ex, "{Interceptor} failed to intercept {EventName}", Name, typeof(T).Name);
//                 }
//             }
//         );
//
//     protected void On<T>(Action<T, InterceptorContext> handler) where T : InterceptorEvent =>
//         On<T>(handler.InvokeAsync);
//
//
//     public async Task<Result<TState>> Handle<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : class {
//
//         await Router.Broadcast(Route.ForType((Type)context.Message.GetType()), context);
//
//     }
// }
//
// /// <summary>
// /// Base class for a functional command service for a given <seealso cref="State{T}"/> type.
// /// Add your command handlers to the service using <see cref="On{TCommand}"/>.
// /// </summary>
// /// <param name="reader">Event reader or event store</param>
// /// <param name="writer">Event writer or event store</param>
// /// <param name="typeMap"><seealso cref="TypeMapper"/> instance or null to use the default type mapper</param>
// /// <param name="amendEvent">Optional function to add extra information to the event before it gets stored</param>
// /// <typeparam name="TState">State object type</typeparam>
// public abstract class CommandService<TState>(IEventReader reader, IEventWriter writer, TypeMapper? typeMap = null, AmendEvent? amendEvent = null)
//     : ICommandService<TState> where TState : State<TState>, new() {
//     readonly TypeMapper          _typeMap  = typeMap ?? TypeMap.Instance;
//     readonly HandlersMap<TState> _handlers = new();
//
//     /// <summary>
//     /// Alternative constructor for the functional command service, which uses an <seealso cref="IEventStore"/> instance for both reading and writing.
//     /// </summary>
//     /// <param name="store">Event store</param>
//     /// <param name="typeMap"><seealso cref="TypeMapper"/> instance or null to use the default type mapper</param>
//     /// <param name="amendEvent">Optional function to add extra information to the event before it gets stored</param>
//     // ReSharper disable once UnusedMember.Global
//     protected CommandService(IEventStore store, TypeMapper? typeMap = null, AmendEvent? amendEvent = null) : this(store, store, typeMap, amendEvent) { }
//
//     /// <summary>
//     /// Returns the command handler builder for the specified command type.
//     /// </summary>
//     /// <typeparam name="TCommand">Command type</typeparam>
//     /// <returns></returns>
//     protected IDefineExpectedState<TCommand, TState> On<TCommand>() where TCommand : class => new CommandHandlerBuilder<TCommand, TState>(this, reader, writer);
//
//     /// <summary>
//     /// Function to handle a command and return the resulting state and changes.
//     /// </summary>
//     /// <param name="command">Command to handle</param>
//     /// <param name="cancellationToken">Cancellation token</param>
//     /// <typeparam name="TCommand">Command type</typeparam>
//     /// <returns><seealso cref="Result{TState}"/> instance</returns>
//     /// <exception cref="ArgumentOutOfRangeException">Throws when there's no command handler was registered for the command type</exception>
//     public async Task<Result<TState>> Handle<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : class {
//         if (!_handlers.TryGet<TCommand>(out var registeredHandler)) {
//             Log.CommandHandlerNotFound<TCommand>();
//             var exception = new Exceptions.CommandHandlerNotFound<TCommand>();
//
//             return Result<TState>.FromError(exception);
//         }
//
//         var streamName     = await registeredHandler.GetStream(command, cancellationToken);
//         var resolvedReader = registeredHandler.ResolveReaderFromCommand(command);
//         var resolvedWriter = registeredHandler.ResolveWriterFromCommand(command);
//
//         try {
//             var loadedState = registeredHandler.ExpectedState switch {
//                 ExpectedState.Any      => await resolvedReader.LoadState<TState>(streamName, false, cancellationToken),
//                 ExpectedState.Existing => await resolvedReader.LoadState<TState>(streamName, true, cancellationToken),
//                 ExpectedState.New      => new(streamName, ExpectedStreamVersion.NoStream, []),
//                 _                      => throw new ArgumentOutOfRangeException(nameof(registeredHandler.ExpectedState), "Unknown expected state")
//             };
//
//             var result = await registeredHandler
//                 .Handler(loadedState.State, loadedState.Events, command, cancellationToken);
//
//             var newEvents = result.ToArray();
//             var newState  = newEvents.Aggregate(loadedState.State, (current, evt) => current.When(evt));
//
//             // Zero in the global position would mean nothing, so the receiver needs to check the Changes.Length
//             if (newEvents.Length == 0) return Result<TState>.FromSuccess(newState, Array.Empty<Change>(), 0);
//
//             var storeResult = await resolvedWriter.Store(streamName, loadedState.StreamVersion, newEvents, Amend, cancellationToken);
//
//             var changes = newEvents.Select(x => new Change(x, _typeMap.GetTypeName(x)));
//             Log.CommandHandled<TCommand>();
//
//             return Result<TState>.FromSuccess(newState, changes, storeResult.GlobalPosition);
//         } catch (Exception e) {
//             Log.ErrorHandlingCommand<TCommand>(e);
//
//             return Result<TState>.FromError(e, $"Error handling command {typeof(TCommand).Name}");
//         }
//
//         NewStreamEvent Amend(NewStreamEvent streamEvent) {
//             var evt = registeredHandler.AmendEvent?.Invoke(streamEvent, command) ?? streamEvent;
//             return amendEvent?.Invoke(evt) ?? evt;
//         }
//     }
//
//     protected static StreamName GetStream(string id) => StreamName.ForState<TState>(id);
//
//     internal void AddHandler<TCommand>(RegisteredHandler<TState> handler) where TCommand : class {
//         _handlers.AddHandlerUntyped(typeof(TCommand), handler);
//     }
// }