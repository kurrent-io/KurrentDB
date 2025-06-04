using System.Net;
using Grpc.Net.Client;
using Kurrent.Surge.Testing.TUnit.Logging;
using KurrentDB.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using static KurrentDB.Protocol.Registry.V2.SchemaRegistryService;

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public class SchemaRegistryServerAutoWireUp {
	public static HttpClient HttpClient { get; private set; } = null!;
	public static SchemaRegistryServiceClient Client { get; private set; } = null!;
	public static ClusterVNodeApp ClusterVNodeApp { get; private set; } = null!;

	public static ClusterVNodeOptions NodeOptions { get; private set; } = null!;
	public static IServiceProvider NodeServices { get; private set; } = null!;

	[Before(Assembly)]
	public static async Task AssemblySetUp(AssemblyHookContext context, CancellationToken cancellationToken) {
		ClusterVNodeApp = new ClusterVNodeApp();

		var (options, services) = await ClusterVNodeApp.Start(configureServices: ConfigureServices);

		NodeServices = services;
		NodeOptions = options;

		var serverUrl = new Uri(ClusterVNodeApp.ServerUrls.First());

		HttpClient = new HttpClient {
			BaseAddress = serverUrl,
			DefaultRequestVersion = HttpVersion.Version20,
			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
		};

		Serilog.Log.Information("Test server started at {Url}", serverUrl);

		Client = new SchemaRegistryServiceClient(
			GrpcChannel.ForAddress(
				$"http://localhost:{serverUrl.Port}",
				new GrpcChannelOptions {
					HttpClient = HttpClient,
					LoggerFactory = NodeServices.GetRequiredService<ILoggerFactory>()
				}
			)
		);
	}

	static void ConfigureServices(IServiceCollection services) {
		var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        services.AddSingleton<ILoggerFactory>(new TestContextAwareLoggerFactory());
		services.AddSingleton<TimeProvider>(timeProvider);
		services.AddSingleton(timeProvider);
	}

	[After(Assembly)]
	public static async Task AssemblyCleanUp() {
		HttpClient.Dispose();
		await ClusterVNodeApp.DisposeAsync();
	}

	[BeforeEvery(Test)]
	public static void TestSetUp(TestContext context) {}

	private class TestContextAwareLoggerFactory : ILoggerFactory {
		readonly ILoggerFactory _defaultLoggerFactory = new LoggerFactory();

		public ILogger CreateLogger(string categoryName) {
			return TestContext.Current is not null
				? TestContext.Current.LoggerFactory().CreateLogger(categoryName)
				: _defaultLoggerFactory.CreateLogger(categoryName);
		}

		public void AddProvider(ILoggerProvider provider) {
			throw new NotImplementedException();
		}

		public void Dispose() {
			_defaultLoggerFactory.Dispose();
		}
	}
}
