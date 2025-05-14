using Grpc.Net.Client;
using Kurrent.Surge.Testing.Containers.KurrentDB;
using Kurrent.Surge.Testing.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using static KurrentDB.Protocol.Registry.V2.SchemaRegistryService;

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public class SchemaRegistryServerAutoWireUp {
    public static KurrentDbTestContainer Container { get; private set; } = null!;

    public static TestServer                  TestServer { get; private set; } = null!;
    public static HttpClient                  HttpClient { get; private set; } = null!;
    public static SchemaRegistryServiceClient Client     { get; private set; } = null!;

    [Before(Assembly)]
    public static async Task AssemblySetUp(AssemblyHookContext context, CancellationToken cancellationToken) {
        Container = new KurrentDbTestContainer();

        await Container.Start();

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ILoggerFactory>(new TestContextAwareLoggerFactory());
        builder.Services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        builder.Services.AddSingleton<TimeProvider>(timeProvider);
        builder.Services.AddSingleton(timeProvider);

        ConfigureServices(builder.Services);

        var app = builder.Build();

        app.UseSchemaRegistryService();

        await app.StartAsync(cancellationToken);

        // // trigger the creation of the connection pool
        // app.Services
        //     .GetRequiredKeyedService<DuckDBConnectionProvider>("schema-registry")
        //     .GetConnection();

        TestServer = (TestServer)app.Services.GetRequiredService<IServer>();
        HttpClient = TestServer.CreateClient();

        Serilog.Log.Information("Test server started at {Url}", HttpClient.BaseAddress);

        Client = new SchemaRegistryServiceClient(
            GrpcChannel.ForAddress(
                HttpClient.BaseAddress!,
                new GrpcChannelOptions {
                    HttpClient    = HttpClient,
                    LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>()
                }
            )
        );
    }

    static void ConfigureServices(IServiceCollection services) {
        var clientSettings = Container.GetClientSettings();

        services.AddEventStoreClient("esdb://admin:changeit@localhost:2113/?tls=false");
        services.AddSingleton(clientSettings);

        services.AddSurgeGrpcComponents();
        services.AddSchemaRegistryService();
    }

    [After(Assembly)]
    public static async Task AssemblyCleanUp() {
        TestServer.Dispose();
        HttpClient.Dispose();
        await Container.DisposeAsync();
    }

    [BeforeEvery(Test)]
    public static void TestSetUp(TestContext context) =>
        Container.ReportStatus();

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