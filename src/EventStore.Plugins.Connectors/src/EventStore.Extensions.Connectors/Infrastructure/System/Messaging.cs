// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

// ReSharper disable CheckNamespace

using DotNext.Collections.Generic;
using EventStore.Core.Messaging;

namespace EventStore.Core.Bus;

public interface IBus : ISubscriber, IPublisher;

public delegate ValueTask HandleMessageAsync<in T>(T message, CancellationToken cancellationToken) where T : Message;

public delegate void HandleMessage<in T>(T message, CancellationToken cancellationToken) where T : Message;

public delegate void DropMessageSubscription();

abstract class MessageHandler<T> : IAsyncHandle<T> where T : Message {
    public abstract ValueTask HandleAsync(T message, CancellationToken token);

    public class Proxy(HandleMessageAsync<T> handler) : MessageHandler<T> {
        public override ValueTask HandleAsync(T message, CancellationToken cancellationToken) => handler(message, cancellationToken);
    }
}

[PublicAPI]
public class MessageModule(ISubscriber subscriber) : IDisposable {
    Dictionary<Type, DropMessageSubscription> Subscriptions { get; } = [];

    public void Dispose() => DropAll();

    protected void On<T>(HandleMessageAsync<T> handler) where T : Message {
        var key = typeof(T);

        if (Subscriptions.ContainsKey(key))
            throw new InvalidOperationException($"Already subscribed to {key.Name}");

        var proxy = new MessageHandler<T>.Proxy(handler);
        subscriber.Subscribe(proxy);
        Subscriptions[key] = () => subscriber.Unsubscribe(proxy);
    }

    protected void On<T>(HandleMessage<T> handler) where T : Message => On<T>((msg, token) => {
        handler(msg, token);
        return ValueTask.CompletedTask;
    });

    protected void Drop<T>() where T : Message {
        if (Subscriptions.Remove(typeof(T), out var unsubscribe))
            unsubscribe();
    }

    protected void DropAll() {
        Subscriptions.Values.ForEach(unsubscribe => unsubscribe());
        Subscriptions.Clear();
    }
}

[PublicAPI]
public static class SubscriberExtensions {
    public static DropMessageSubscription On<T>(this ISubscriber subscriber, HandleMessageAsync<T> handler) where T : Message {
        var proxy = new MessageHandler<T>.Proxy(handler);
        subscriber.Subscribe(proxy);
        return () => subscriber.Unsubscribe(proxy);
    }

    public static DropMessageSubscription On<T>(this ISubscriber subscriber, HandleMessage<T> handler) where T : Message =>
        On<T>(subscriber,
            (msg, token) => {
                handler(msg, token);
                return ValueTask.CompletedTask;
            });
}