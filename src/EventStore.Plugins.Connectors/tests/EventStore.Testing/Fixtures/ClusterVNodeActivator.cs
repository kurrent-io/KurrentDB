using System.ComponentModel;
using System.Net;
using EventStore.ClusterNode;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Configuration;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using Microsoft.Extensions.Configuration;
using Serilog.Debugging;

namespace EventStore.Testing.Fixtures;

public static class ClusterVNodeActivator {
    static ClusterVNodeActivator() {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> {
                    { "EventStore:Application:TelemetryOptout", "true" },
                    { "EventStore:Application:Insecure", "true" },
                    { "EventStore:Database:MemDb", "true" },
                    { "EventStore:Cluster:LeaderElectionTimeoutMs", "10000" },
                    { "EventStore:Logging:LogLevel", "Verbose" },
                    { "EventStore:Logging:DisableLogFile", "false" },
                    { "EventStore:Interface:DisableAdminUi", "false" },
                    { "EventStore:DevMode:Dev", "true" }
                }
            )
            .Build();

        NodeOptions = configuration.GetClusterVNodeOptions();

        var service = new ClusterVNodeHostedService(NodeOptions, null, NodeOptions.ConfigurationRoot);

        // AppDomain.CurrentDomain.ProcessExit  += (_, _) => Dispose();
        // AppDomain.CurrentDomain.DomainUnload += (_, _) => Dispose();

        NodeSystemMonitor.WaitUntilReady(service.Node).GetAwaiter().GetResult();

        Publisher  = service.Node.MainQueue;
        Subscriber = service.Node.MainBus;

        return;

        void Dispose() {
            try {
                service.Node.StopAsync().GetAwaiter().GetResult();
                service.Dispose();
            }
            catch (Exception) {
                // ignored
            }
        }
    }

    public static ClusterVNodeOptions NodeOptions { get; }
    public static IPublisher          Publisher   { get; }
    public static ISubscriber         Subscriber  { get; }

    public static void Initialize() => SelfLog.WriteLine("ClusterVNodeActivator initialized");

    /// <summary>
    /// Attempts to bind the configuration to a new instance of <see cref="ClusterVNodeOptions"/>,
    /// assuming that all the options are under the "EventStore" section and use fully named keys.<br/>
    /// Examples: EventStore:Application:TelemetryOptout, EventStore:Database:MemDb
    /// </summary>
    public static ClusterVNodeOptions GetClusterVNodeOptions(this IConfigurationRoot configurationRoot) {
        // required because of a bug in the configuration system that
        // is not reading the attribute from the property itself
        TypeDescriptor.AddAttributes(typeof(EndPoint[]), new TypeConverterAttribute(typeof(GossipSeedConverter)));
        TypeDescriptor.AddAttributes(typeof(IPAddress), new TypeConverterAttribute(typeof(IPAddressConverter)));

        // because we use full keys everything is mapped correctly
        return (configurationRoot.GetRequiredSection("EventStore").Get<ClusterVNodeOptions>() ?? new()) with {
            ConfigurationRoot = configurationRoot
            // Unknown           = ClusterVNodeOptions.UnknownOptions.FromConfiguration(configurationRoot.GetRequiredSection("EventStore")),
            // LoadedOptions     = ClusterVNodeOptions.GetLoadedOptions(configurationRoot)
        };
    }

    public class NodeSystemMonitor : IHandle<SystemMessage.BecomeLeader>, IHandle<SystemMessage.StateChangeMessage>, IDisposable {
        NodeSystemMonitor(ClusterVNode node) {
            Node  = node;
            Ready = new();

            Node.MainBus.Subscribe<SystemMessage.BecomeLeader>(this);
            Node.MainBus.Subscribe<SystemMessage.StateChangeMessage>(this);
        }

        ClusterVNode         Node  { get; }
        TaskCompletionSource Ready { get; }

        public void Dispose() {
            try {
                Node.MainBus.Unsubscribe<SystemMessage.BecomeLeader>(this);
                Node.MainBus.Unsubscribe<SystemMessage.StateChangeMessage>(this);
            }
            catch (Exception) {
                // ignored
            }
        }

        void IHandle<SystemMessage.BecomeLeader>.Handle(SystemMessage.BecomeLeader message) {
            Ready.TrySetResult();
            Node.MainBus.Unsubscribe<SystemMessage.BecomeLeader>(this);
        }

        public void Handle(SystemMessage.StateChangeMessage message) {
            if (message.State == VNodeState.Leader) {
                Ready.TrySetResult();
                Node.MainBus.Unsubscribe<SystemMessage.StateChangeMessage>(this);
            }
        }

        public async Task WaitUntilReady(TimeSpan? timeout = null) {
            using var cancellator = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));

            cancellator.Token
                .Register(() => Ready.TrySetException(new Exception("Node not ready in time.")));

            await Node.StartAsync(false);

            await Ready.Task;
        }

        public static async Task WaitUntilReady(ClusterVNode node, TimeSpan? timeout = null) {
            using var monitor = new NodeSystemMonitor(node);
            await monitor.WaitUntilReady(timeout);
        }
    }
}