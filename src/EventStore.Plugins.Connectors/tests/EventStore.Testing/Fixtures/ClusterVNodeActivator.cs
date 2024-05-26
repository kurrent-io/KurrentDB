using System.ComponentModel;
using System.Net;
using EventStore.ClusterNode;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Configuration;
using EventStore.Core.Messages;
using Microsoft.Extensions.Configuration;
using Serilog.Debugging;

namespace EventStore.Testing.Fixtures;

public static class ClusterVNodeActivator {
	static ClusterVNodeActivator() {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "EventStore:Application:TelemetryOptout", "true" },
				{ "EventStore:Application:Insecure", "true" },
				{ "EventStore:Database:MemDb", "true" },
			})
			.Build();
			
		NodeOptions = configuration.GetClusterVNodeOptions();
		
		var service = new ClusterVNodeHostedService(NodeOptions, null, NodeOptions.ConfigurationRoot);
		
		AppDomain.CurrentDomain.ProcessExit  += (_, _) => Dispose();
		AppDomain.CurrentDomain.DomainUnload += (_, _) => Dispose();

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

	class NodeSystemMonitor : IHandle<SystemMessage.BecomeLeader>, IDisposable {
		NodeSystemMonitor(ClusterVNode node) {
			Node  = node;
			Ready = new();

			Node.MainBus.Subscribe(this);
		}

		ClusterVNode         Node  { get; }
		TaskCompletionSource Ready { get; }
		
		void IHandle<SystemMessage.BecomeLeader>.Handle(SystemMessage.BecomeLeader message) {
			Ready.TrySetResult();
			Node.MainBus.Unsubscribe(this);
		}

		public void Dispose() {
			try {
				Node.MainBus.Unsubscribe(this);
			}
			catch (Exception) {
				// ignored
			}
		}

		public async Task WaitUntilReady(TimeSpan? timeout = null) {
			using var cancellator = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
			
			cancellator.Token
				.Register(() => Ready.TrySetException(new Exception("Node not ready in time.")));
			
			await Node.StartAsync(waitUntilRead: true);
			
			await Ready.Task;
		}
		
		public static async Task WaitUntilReady(ClusterVNode node, TimeSpan? timeout = null) {
			using var monitor = new NodeSystemMonitor(node);
			await monitor.WaitUntilReady(timeout);
		}
	}
	
	/// <summary>
	/// Attempts to bind the configuration to a new instance of <see cref="ClusterVNodeOptions"/>,
	/// assuming that all the options are under the "EventStore" section and use fully named keys.<br/>
	/// Examples: EventStore:Application:TelemetryOptout, EventStore:Database:MemDb
	/// </summary>
	static ClusterVNodeOptions GetClusterVNodeOptions(this IConfigurationRoot configurationRoot) {
		// required because of a bug in the configuration system that
		// is not reading the attribute from the property itself
		TypeDescriptor.AddAttributes(typeof(EndPoint[]), new TypeConverterAttribute(typeof(GossipSeedConverter)));
		TypeDescriptor.AddAttributes(typeof(IPAddress), new TypeConverterAttribute(typeof(IPAddressConverter)));

		// because we use full keys everything is mapped correctly
		return (configurationRoot.GetRequiredSection("EventStore").Get<ClusterVNodeOptions>() ?? new()) with {
			ConfigurationRoot = configurationRoot,
			// Unknown           = ClusterVNodeOptions.UnknownOptions.FromConfiguration(configurationRoot.GetRequiredSection("EventStore")),
			// LoadedOptions     = ClusterVNodeOptions.GetLoadedOptions(configurationRoot)
		};
	}
}