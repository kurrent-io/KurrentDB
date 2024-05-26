// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Core;

public abstract class MessageHandler<T> : IHandle<T> where T : Message {
    protected SystemClient Client { get; private set; } = null!;
    
    public abstract Task Handle(T message, CancellationToken cancellationToken);
    
    void IHandle<T>.Handle(T message) {
        Handle(message, CancellationToken.None).GetAwaiter().GetResult();
    }
    
    public void Subscribe(IBus bus) {
        bus.Subscribe(this);

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        Client ??= new(bus);
    }

    public void Unsubscribe(IBus bus) => bus.Unsubscribe(this);
}

public abstract class SyncMessageHandler<T> : IHandle<T> where T : Message {
    protected SystemClient Client { get; private set; }

    public abstract void Handle(T message);
    
    public void Subscribe(IBus bus) {
        bus.Subscribe(this);
        
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        Client ??= new(bus);
    }

    public void Unsubscribe(IBus bus) => bus.Unsubscribe(this);
}