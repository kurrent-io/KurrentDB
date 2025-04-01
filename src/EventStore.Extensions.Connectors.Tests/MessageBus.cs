using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Connectors.Tests;

public class MessageBus(string? name = null) : InMemoryBus(name ?? "test-bus", false), IPublisher {
    public void Publish(Message message) => DispatchAsync(message).AsTask().GetAwaiter().GetResult();

    public void Subscribe<T>(HandleMessageAsync<T> handler) where T : Message =>
        Subscribe(new MessageHandler<T>.Proxy(handler));

    public void Subscribe<T>(HandleMessage<T> handler) where T : Message =>
        Subscribe<T>((msg, ct) => {
            handler(msg, ct);
            return ValueTask.CompletedTask;
        });

    public Task SubscribeAndWait<T>(HandleMessageAsync<T> handler, CancellationToken timeoutToken = default) where T : Message {
    	var completion = new TaskCompletionSource();

    	timeoutToken.Register(() => completion.SetCanceled(timeoutToken));

    	Subscribe(new MessageHandler<T>.Proxy(async (msg, ct) => {
    		try {
    			await handler(msg, ct);
    			completion.SetResult();
    		}
    		catch (Exception ex) {
    			completion.SetException(ex);
    		}
    	}));

    	return completion.Task;
    }

    public Task SubscribeAndWait<T>(HandleMessage<T> handler, CancellationToken timeoutToken = default) where T : Message =>
    	SubscribeAndWait<T>((msg, ct) => {
    		handler(msg, ct);
    		return ValueTask.CompletedTask;
    	}, timeoutToken);
}