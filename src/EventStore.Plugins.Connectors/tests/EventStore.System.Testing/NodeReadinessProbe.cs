using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Toolkit.Testing;
using Serilog;

namespace EventStore.System.Testing;

public class NodeReadinessProbe : IHandle<SystemMessage.SystemReady> {
    static readonly ILogger Log = Serilog.Log.Logger.ForContext<NodeReadinessProbe>();

    TaskCompletionSource Ready { get; } = new();

    void IHandle<SystemMessage.SystemReady>.Handle(SystemMessage.SystemReady message) {
        if (!Ready.Task.IsCompleted)
            Ready.TrySetResult();
    }

    async Task WaitUntilReadyAsync(ClusterVNode node, TimeSpan? timeout = null) {
        node.MainBus.Subscribe(this);

        using var cancellator = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30)).With(x => {
            x.Token.Register(() => {
                Ready.TrySetException(new Exception("Node not ready in time."));
                node.MainBus.Unsubscribe(this);
            });
        });

        await Ready.Task.ContinueWith(t => {
            if (t.IsCompletedSuccessfully) {
                Log.Verbose("Node is ready.");
                node.MainBus.Unsubscribe(this);
                Log.Verbose("Unsubscribed from the bus.");
            }
        });
    }

    public static Task WaitUntilReady(ClusterVNode node, TimeSpan? timeout = null) =>
        new NodeReadinessProbe().WaitUntilReadyAsync(node, timeout);
}