// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using System.Net;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Control;
using EventStore.Extensions.Connectors.Tests;
using Microsoft.Extensions.DependencyInjection;
using MemberInfo = EventStore.Core.Cluster.MemberInfo;

namespace EventStore.Connectors.Tests.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsControlRegistryTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    static readonly MessageBus MessageBus = new();

    static readonly MemberInfo FakeMemberInfo = MemberInfo.ForManager(Guid.NewGuid(), DateTime.Now, true, new IPEndPoint(0, 0));

    // [Fact]
    public Task returns_active_connectors_and_updates_snapshot() => Fixture.TestWithTimeout(TimeSpan.FromMinutes(5),
        async cancellator => {
            // Arrange
            var options = new ConnectorsControlRegistryOptions {
                Filter           = ConnectorsFeatureConventions.Filters.ManagementFilter,
                SnapshotStreamId = $"{ConnectorsFeatureConventions.Streams.ControlConnectorsRegistryStream}/{Fixture.NewIdentifier("test")}"
            };

            var getReaderBuilder   = Fixture.NodeServices.GetRequiredService<Func<SystemReaderBuilder>>();
            var getProducerBuilder = Fixture.NodeServices.GetRequiredService<Func<SystemProducerBuilder>>();

            var sut = new ConnectorsControlRegistry(options, getReaderBuilder, getProducerBuilder, TimeProvider.System);




            var result = await sut.GetConnectors(cancellator.Token);
        });
}