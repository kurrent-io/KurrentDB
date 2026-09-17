// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Connectors.Tests.Infrastructure;

public class SystemConnectorsFactoryTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public Task counts_only_disposed_connector_as_closed() => Fixture.TestWithTimeout(async cts => {
        // Arrange
        var factory = Fixture.NodeServices.GetRequiredService<ISystemConnectorFactory>();

        var firstId  = ConnectorId.From(Fixture.NewConnectorId());
        var secondId = ConnectorId.From(Fixture.NewConnectorId());

        var closed = new List<string>();

        using var listener = new MeterListener {
            InstrumentPublished = (instrument, meterListener) => {
                if (instrument.Meter.Name == "Kurrent.Connectors" && instrument.Name == "kurrent_connector_active_total")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<int>((_, measurement, tags, _) => {
            if (measurement >= 0)
                return;

            var connectorId = tags.ToArray().FirstOrDefault(tag => tag.Key == "connector_id").Value?.ToString();
            if (connectorId is not null)
                closed.Add(connectorId);
        });

        listener.Start();

        var first  = factory.CreateConnector(firstId, SerilogSinkSettings());
        var second = factory.CreateConnector(secondId, SerilogSinkSettings());

        await first.Connect(cts.Token);
        await second.Connect(cts.Token);

        // Act
        await first.DisposeAsync();

        // Assert
        closed.Should().Equal(firstId.ToString());

        await second.DisposeAsync();

        closed.Should().Equal(firstId.ToString(), secondId.ToString());
    });

    static IConfiguration SerilogSinkSettings() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new("InstanceTypeName", "serilog-sink")])
            .Build();
}
