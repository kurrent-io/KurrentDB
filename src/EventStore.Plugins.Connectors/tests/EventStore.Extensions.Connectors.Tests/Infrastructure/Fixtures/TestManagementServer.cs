using EventStore.Extensions.Connectors.Management.TestServer;
using EventStore.Extensions.Connectors.Tests.EventStore.Plugins;
using EventStore.Extensions.Connectors.Tests.Eventuous;
using EventStore.Plugins.Authorization;
using EventStore.Testing.Http;
using Eventuous;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace EventStore.Extensions.Connectors.Tests.Fixtures;

[PublicAPI]
public class TestManagementServer(ITestOutputHelper output)
    : TestServerContext<TestManagementServerEntrypoint>(output, TestServerStartMode.StartHost) {
    protected override void ConfigureServices(IServiceCollection services) =>
        services
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddSingleton<FakeAuthorizationProvider>()
            .AddSingleton<IAuthorizationProvider>(sp => sp.GetRequiredService<FakeAuthorizationProvider>())
            .RemoveAll<IEventStore>() // So that we can use InMemoryEventStore.
            .AddAggregateStore<InMemoryEventStore>();
}