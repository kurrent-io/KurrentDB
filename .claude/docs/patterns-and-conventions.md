# Patterns and Conventions Reference

> Read this when: implementing new features that interact with the message bus, enumerators, authorization, or secondary indexes.

## Enumerators and Expiry Strategy

**All read enumerators now use `IExpiryStrategy` instead of `DateTime deadline`**:

```csharp
// Correct (new pattern)
new ReadAllForwards(
    bus, position, maxCount, resolveLinks, user, requiresLeader,
    DefaultExpiryStrategy.Instance,  // Not a DateTime!
    cancellationToken
);

// Old pattern (don't use)
new ReadAllForwards(..., DateTime.UtcNow.AddMinutes(5), ct);
```

Available implementations:
- `DefaultExpiryStrategy.Instance`: Returns null expiry, which makes reads default to `Created + ESConsts.ReadRequestTimeout` (10,000ms)
- Custom implementations: Implement `IExpiryStrategy` interface

## Message Bus Patterns

**Awaiting a response — use `TcsEnvelope<>`:**

When you need to publish a message and await the response, **always use `TcsEnvelope<T>`** instead of manually creating a `TaskCompletionSource` + `CallbackEnvelope`. The `CallbackEnvelope` callback runs on the bus dispatch thread — if you call `TrySetResult` there, the `await` continuation can resume on the bus thread and block it (deadlock risk).

```csharp
// CORRECT: TcsEnvelope uses RunContinuationsAsynchronously internally
var envelope = new TcsEnvelope<WriteEventsCompleted>();
publisher.Publish(new ClientMessage.WriteEvents(
    internalCorrId: corrId,
    correlationId: corrId,
    envelope: envelope,
    requireLeader: true,
    user: user));
var result = await envelope.Task;

// WRONG: continuation runs on bus thread, can deadlock
var tcs = new TaskCompletionSource<WriteEventsCompleted>();
publisher.Publish(new ClientMessage.WriteEvents(
    envelope: new CallbackEnvelope(msg => tcs.TrySetResult((WriteEventsCompleted)msg)),
));
var result = await tcs.Task;
```

**Fire-and-forget callbacks — `CallbackEnvelope` is fine:**
```csharp
var envelope = new CallbackEnvelope(OnMessage);
publisher.Publish(new ReadStreamEventsForward(
    correlationId: Guid.NewGuid(),
    envelope: envelope,
    streamId: streamName,
));
```

**Prefer `ISystemClient` over raw `IPublisher` for business logic writes:**
```csharp
// CORRECT: high-level, handles correlation IDs, envelopes, etc.
await client.Writing.WriteEvents(writes, requireLeader: true, user);

// AVOID: 20+ lines of manual ClientMessage.WriteEvents construction
publisher.Publish(new ClientMessage.WriteEvents(
    internalCorrId: corrId, correlationId: corrId,
    envelope: envelope, requireLeader: true,
    eventStreamIds: streamIds.ToArray(), ...));
```

## Authorization Patterns

**Check access before operations**:
```csharp
var operation = WriteOperation.WithParameter(
    Operations.Streams.Parameters.StreamId(streamName)
);

var hasAccess = await authorizationProvider.CheckAccessAsync(
    user, operation, cancellationToken
);

if (!hasAccess)
    throw RpcExceptions.AccessDenied(operation.Name);
```

**Thread the `ClaimsPrincipal` through — never hardcode to System:**

When writing events on behalf of a projection or service, pass the actual `ClaimsPrincipal` from the projection's `RunAs` configuration, not `SystemAccounts.System`. Always pass `requireLeader` explicitly rather than defaulting to `false`.

## Working with Secondary Indexes

**Reading from indexes**:
```csharp
var enumerator = new IndexSubscription(
    bus: publisher,
    expiryStrategy: DefaultExpiryStrategy.Instance,
    checkpoint: Position.Start,
    indexName: "$ce-myCategory",
    user: user,
    requiresLeader: false,
    cancellationToken: ct
);

await foreach (var response in enumerator) {
    if (response is ReadResponse.EventReceived eventReceived) {
        // Process event
    }
}
```

**Index naming conventions**:
- `$idx`: Default index (all events)
- `$ce-<category>`: Category index
- `$et-<eventType>`: Event type index

**Reading from Secondary Indexes (step-by-step)**:
1. Use `IndexSubscription` enumerator for live reads
2. Use `ReadIndexEventsForward` for batch reads
3. Index names follow convention above
4. Always handle `ReadResponse.Checkpoint` for resumption
