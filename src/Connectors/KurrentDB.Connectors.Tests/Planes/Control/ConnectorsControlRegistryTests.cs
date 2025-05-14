// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using System.Net;
using KurrentDB.Connectors.Planes;
using KurrentDB.Connectors.Planes.Control;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using Microsoft.Extensions.DependencyInjection;
using MemberInfo = KurrentDB.Core.Cluster.MemberInfo;

namespace KurrentDB.Connectors.Tests.Planes.Control;

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
