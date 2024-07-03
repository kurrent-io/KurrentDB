// using EventStore.Connectors.ControlPlane.Assignment;
// using EventStore.Connectors.ControlPlane.Coordination;
// using EventStore.Core.Bus;
// using EventStore.Core.Services;
// using EventStore.Streaming.Consumers;
// using EventStore.Streaming.Processors;
// using EventStore.Streaming.Readers;
// using Microsoft.AspNetCore.Mvc.Testing;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
//
// namespace EventStore.Extensions.Connectors.Tests.ControlPlane.Coordination;
//
// [Trait("Category", "Integration")]
// [Trait("ControlPlane", "Coordination")]
// public class ConnectorsCoordinatorTests(ITestOutputHelper output, StreamingFixture fixture) : StreamingTests(output, fixture) {
// 	[Fact]
// 	public async Task works() {
// 		// Arrange
//
// 		await using var webAppFactory = new WebApplicationFactory<ClusterNode.Program>();
//
// 		webAppFactory.WithWebHostBuilder(builder => {
// 			builder.ConfigureServices(services => {
// 				services.AddSingleton(Fixture.Publisher);
// 				services.AddSingleton(Fixture.Subscriber);
// 				services.AddSingleton(Fixture.NodeOptions);
//
// 				services.AddSingleton<SystemReader>(
// 					serviceProvider => {
// 						var publisher     = serviceProvider.GetRequiredService<IPublisher>();
// 						var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
//
// 						var reader = SystemReader.Builder
// 							.ReaderName("connectors-reader")
// 							.Publisher(publisher)
// 							.LoggerFactory(loggerFactory)
// 							.EnableLogging()
// 							.Create();
//
// 						return reader;
// 					}
// 				);
//
// 				services.AddSingleton<ConnectorsManagementClient>();
// 				services.AddSingleton<ConnectorAssignorProvider>();
// 			});
// 		});
//
//
// 		// Act
// 		var processor = CreateProcessor(webAppFactory.Services);
//
// 		// Assert
//
// 	}
//
// 	public async Task works_2() {
// 		// Arrange
//
// 		var services = new ServiceCollection();
//
// 		services.AddSingleton(Fixture.Publisher);
// 		services.AddSingleton(Fixture.Subscriber);
// 		services.AddSingleton(Fixture.NodeOptions);
//
// 		services.AddSingleton<SystemReader>(
// 			ctx => {
// 				var publisher = ctx.GetRequiredService<IPublisher>();
// 				var loggerFactory = ctx.GetRequiredService<ILoggerFactory>();
//
// 				var reader = SystemReader.Builder
// 					.ReaderName("connectors-reader")
// 					.Publisher(publisher)
// 					.LoggerFactory(loggerFactory)
// 					.EnableLogging()
// 					.Create();
//
// 				return reader;
// 			}
// 		);
//
// 		services.AddSingleton<ConnectorsManagementClient>();
// 		services.AddSingleton<ConnectorAssignorProvider>();
//
// 		var serviceProvider = services.BuildServiceProvider();
//
// 		// Act
// 		var processor = CreateProcessor(serviceProvider);
//
// 		// Assert
//
// 	}
//
// 	static SystemProcessor CreateProcessor(IServiceProvider serviceProvider) {
// 		var publisher              = serviceProvider.GetRequiredService<IPublisher>();
// 		var loggerFactory          = serviceProvider.GetRequiredService<ILoggerFactory>();
// 		var timeProvider           = serviceProvider.GetRequiredService<TimeProvider>();
// 		var assignorProvider       = serviceProvider.GetRequiredService<ConnectorAssignorProvider>();
// 		var managementClient       = serviceProvider.GetRequiredService<ConnectorsManagementClient>();
//
// 		var coordinator = new ConnectorsCoordinator(assignorProvider.Get, managementClient.ListActiveConnectors);
//
// 		string[] streams = [
// 			// reacts to cluster topology changes
// 			SystemStreams.GossipStream,
// 			// reacts to task manager events: started, stopped, etc.
// 			"$connectors-activation",
// 			// reacts to connector events: activating, deactivating, etc.
// 			"$connectors-"
// 		];
//
// 		var processor = SystemProcessor.Builder
// 			.ProcessorName("connectors-coordinator")
// 			.Streams(streams)
// 			.DefaultOutputStream("$connectors-activation-requests")
// 			.InitialPosition(SubscriptionInitialPosition.Earliest)
// 			.DisableAutoCommit()
// 			.Publisher(publisher)
// 			.LoggerFactory(loggerFactory)
// 			.EnableLogging()
// 			.WithModule(coordinator)
// 			.Create();
//
// 		return processor;
// 	}
// }