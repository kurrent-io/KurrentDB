using EventStore.Extensions.Connectors.Management.TestServer;
using EventStore.Extensions.Connectors.Tests.EventStore.Plugins;
using EventStore.Plugins.Authorization;
using EventStore.Testing.Http;
using Eventuous;
using Eventuous.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace EventStore.Extensions.Connectors.Tests.Fixtures;

[PublicAPI]
public class TestManagementServer(ITestOutputHelper output) : TestServerContext<TestManagementServerEntrypoint>(output, TestServerStartMode.StartHost) {
    protected override void ConfigureServices(IServiceCollection services) =>
        services
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddSingleton<IAuthorizationProvider>(new FakeAuthorizationProvider())
            .RemoveAll<IEventStore>() // So that we can use InMemoryEventStore.
            .AddAggregateStore<InMemoryEventStore>();
}