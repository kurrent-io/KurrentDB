// ReSharper disable ArrangeTypeMemberModifiers

using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core;
using KurrentDB.Testing.TUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Tests.Fixtures;

[PublicAPI]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed partial class ClusterVNodeTestContext : IAsyncInitializer, IAsyncDisposable {
    public ClusterVNodeTestContext() {
        Server = new ClusterVNodeApp(ConfigureServices);

        ServerOptions = Server.ServerOptions;
        Services      = Server.Services;
    }

    static void ConfigureServices(ClusterVNodeOptions options, IServiceCollection services) {
        services
            .AddSingleton<ILoggerFactory, ToolkitTestLoggerFactory>()
            //.AddTestLogging()
            .AddTestTimeProvider();

        // Configures gRPC client address discovery to use a static resolver
        // that points to the test server's address.
        services.EnableGrpcClientsAddressDiscovery();

        // ====================================================================
        // Adds gRPC clients for every service that is available on the server
        // ====================================================================
        services.AddGrpcClient<StreamsServiceClient>();
    }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    ClusterVNodeApp Server { get; set; }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    public ClusterVNodeOptions ServerOptions { get; }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Pre-configured Faker instance for generating test data.
    /// </summary>
    public ILogger Logger => TestContext.Current.Logger();

    /// <summary>
    /// Pre-configured Faker instance for generating test data.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestContext.Current.LoggerFactory();

    /// <summary>
    /// The time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time { get; private set; } = null!;

    /// <summary>
    /// The client for interacting with the system bus.
    /// </summary>
    public ISystemClient SystemClient { get; private set; } = null!;

    /// <summary>
	/// The gRPC client for the Streams service.
	/// </summary>
	public StreamsServiceClient StreamsClient { get; private set; } = null!;

    /// <summary>
    /// Initializes the test server and related resources.
    /// This method sets up the server, starts it, and
    /// configures all the grpc service clients.
    /// </summary>
    public async Task InitializeAsync() {
        await Server.Start();

        Time         = Server.Services.GetRequiredService<FakeTimeProvider>();
        SystemClient = Server.Services.GetRequiredService<ISystemClient>();

        // ====================================================================
        // Resolve all gRPC clients
        // ====================================================================

        StreamsClient = Server.Services.GetRequiredService<StreamsServiceClient>();
    }

    public async ValueTask DisposeAsync() {
        await Server.DisposeAsync();
    }
}

class ToolkitTestLoggerFactory : ILoggerFactory {
    readonly ILoggerFactory _defaultLoggerFactory = new LoggerFactory();

    public ILogger CreateLogger(string categoryName) {
        return TestContext.Current.TryGetLoggerFactory(out var factory)
            ? factory.CreateLogger(categoryName)
            : _defaultLoggerFactory.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider) =>
        throw new NotImplementedException();

    public void Dispose() {
        // No-op; the TestExecutor is responsible for disposing of any logger providers
    }
}

//
// [ProviderAlias("TUnit")]
// public sealed class ToolkitTestLoggerProvider : ILoggerProvider
// {
//     public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
//         TUnitLoggerWrapper.Instance;
//
//     public void Dispose()
//     {
//     }
// }
