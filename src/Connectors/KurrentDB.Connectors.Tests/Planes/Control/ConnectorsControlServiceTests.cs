// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Linq;
using System.Threading.Channels;
using EventStore.Plugins.Licensing;
using Eventuous.Testing;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.Producers;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Planes.Control;
using KurrentDB.Connectors.Planes.Control.Model;
using KurrentDB.Connectors.Planes.Management;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Surge.Consumers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static KurrentDB.Connectors.Planes.ConnectorsFeatureConventions;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsControlServiceTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public Task saves_snapshot_again_after_interval_while_leader() => Fixture.TestWithTimeout(async cancellator => {
        var time  = new FakeTimeProvider();
        var saves = Channel.CreateUnbounded<RecordPosition>();

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellator.Token);

        var start = (await Fixture.ProduceTestEvents(Fixture.NewStreamId(), numberOfRequests: 1, batchSize: 1)).Last().Position;

        var sut = ActivatorUtilities.CreateInstance<ExecutableConnectorsControlService>(
            Fixture.NodeServices,
            new ConnectorsActivator(CreateConnector),
            (LoadActiveConnectorsSnapshot)(_ => Task.FromResult(new ActiveConnectors([], start))),
            (SaveActiveConnectorsSnapshot)(connectors => {
                saves.Writer.TryWrite(connectors.Position);
                return Task.CompletedTask;
            }),
            (TimeProvider)time
        );

        var nodeInfo = await Fixture.NodeServices.GetRequiredService<GetNodeSystemInfo>()(cancellator.Token);
        var running  = sut.Run(nodeInfo, stopping.Token);

        var first  = await NextSaveAfterInterval();
        var second = await NextSaveAfterInterval();

        second.LogPosition.Should().BeGreaterThan(first.LogPosition);

        await stopping.CancelAsync();
        await running;

        return;

        async Task<RecordPosition> NextSaveAfterInterval() {
            time.Advance(TimeSpan.FromMinutes(1));
            await Fixture.ProduceTestEvents(Fixture.NewStreamId(), numberOfRequests: 11, batchSize: 100);
            return await saves.Reader.ReadAsync(cancellator.Token);
        }

        IConnector CreateConnector(ConnectorId connectorId, IDictionary<string, string?> settings) {
            var connector = new TestConnector();
            stopping.Token.Register(() => _ = connector.DisposeAsync().AsTask());
            return connector;
        }
    });

    [Fact]
    public Task saves_snapshot_on_first_checkpoint_after_taking_leadership() => Fixture.TestWithTimeout(async cancellator => {
        var saved = new TaskCompletionSource<ActiveConnectors>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellator.Token);

        var start = (await Fixture.ProduceTestEvents(Fixture.NewStreamId(), numberOfRequests: 1, batchSize: 1)).Last().Position;

        var sut = ActivatorUtilities.CreateInstance<ExecutableConnectorsControlService>(
            Fixture.NodeServices,
            new ConnectorsActivator(CreateConnector),
            (LoadActiveConnectorsSnapshot)(_ => Task.FromResult(new ActiveConnectors([], start))),
            (SaveActiveConnectorsSnapshot)(connectors => {
                saved.TrySetResult(connectors);
                return Task.CompletedTask;
            }),
            (TimeProvider)new FakeTimeProvider()
        );

        var nodeInfo = await Fixture.NodeServices.GetRequiredService<GetNodeSystemInfo>()(cancellator.Token);
        var running  = sut.Run(nodeInfo, stopping.Token);

        await Fixture.ProduceTestEvents(Fixture.NewStreamId(), numberOfRequests: 11, batchSize: 100);

        var snapshot = await saved.Task.WaitAsync(cancellator.Token);

        snapshot.Position.LogPosition.Should().BeGreaterThan(start.LogPosition);

        await stopping.CancelAsync();
        await running;

        return;

        IConnector CreateConnector(ConnectorId connectorId, IDictionary<string, string?> settings) {
            var connector = new TestConnector();
            stopping.Token.Register(() => _ = connector.DisposeAsync().AsTask());
            return connector;
        }
    });

    [Fact]
    public Task activates_connectors_only_after_catching_up() => Fixture.TestWithTimeout(async cancellator => {
        var staleId       = Fixture.NewConnectorId();
        var activeId      = Fixture.NewConnectorId();
        var deactivatedId = Fixture.NewConnectorId();
        var liveId        = Fixture.NewConnectorId();
        var activations   = Channel.CreateUnbounded<string>();

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellator.Token);

        await Produce(staleId, new ConnectorActivating { ConnectorId = staleId, Revision = 1 });

        var snapshot = await Fixture.ProduceTestEvents(Fixture.NewStreamId(), numberOfRequests: 1, batchSize: 1);

        await Produce(activeId, new ConnectorActivating { ConnectorId = activeId, Revision = 1 });
        await Produce(deactivatedId, new ConnectorActivating { ConnectorId = deactivatedId, Revision = 1 });
        await Produce(deactivatedId, new ConnectorDeactivating { ConnectorId = deactivatedId });

        var sut = ActivatorUtilities.CreateInstance<ExecutableConnectorsControlService>(
            Fixture.NodeServices,
            new ConnectorsActivator(CreateConnector),
            (LoadActiveConnectorsSnapshot)(_ => Task.FromResult(new ActiveConnectors([], snapshot.Last().Position))),
            (SaveActiveConnectorsSnapshot)(_ => Task.CompletedTask)
        );

        var nodeInfo = await Fixture.NodeServices.GetRequiredService<GetNodeSystemInfo>()(cancellator.Token);
        var running  = sut.Run(nodeInfo, stopping.Token);

        (await NextActivation()).Should().Be(activeId);

        await Produce(liveId, new ConnectorActivating { ConnectorId = liveId, Revision = 1 });

        (await NextActivation()).Should().Be(liveId);

        await stopping.CancelAsync();
        await running;

        return;

        IConnector CreateConnector(ConnectorId connectorId, IDictionary<string, string?> settings) {
            var connector = new TestConnector();
            stopping.Token.Register(() => _ = connector.DisposeAsync().AsTask());
            activations.Writer.TryWrite(connectorId);
            return connector;
        }

        async Task<string> NextActivation() {
            string[] ours = [staleId, activeId, deactivatedId, liveId];

            while (true) {
                var connectorId = await activations.Reader.ReadAsync(cancellator.Token);
                if (ours.Contains(connectorId))
                    return connectorId;
            }
        }

        ValueTask<ProduceResult> Produce(string connectorId, object evt) =>
            Fixture.Producer.Produce(ProduceRequest.Builder.Message(evt).Stream(Streams.GetManagementStream(connectorId)).Create());
    });

    class ExecutableConnectorsControlService(
        IPublisher publisher,
        ISubscriber subscriber,
        ISystemClient client,
        ConnectorsActivator activator,
        LoadActiveConnectorsSnapshot loadActiveConnectorsSnapshot,
        SaveActiveConnectorsSnapshot saveActiveConnectorsSnapshot,
        GetNodeSystemInfo getNodeSystemInfo,
        Func<SystemConsumerBuilder> getConsumerBuilder,
        TimeProvider time,
        ILoggerFactory loggerFactory
    ) : ConnectorsControlService(
        publisher, subscriber, client, activator, NewCommandApplication(time), loadActiveConnectorsSnapshot,
        saveActiveConnectorsSnapshot, getNodeSystemInfo, getConsumerBuilder, time, loggerFactory
    ) {
        public Task Run(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) => Execute(nodeInfo, stoppingToken);

        static ConnectorsCommandApplication NewCommandApplication(TimeProvider time) => new(
            _ => new ValidationResult(),
            (_, settings) => settings,
            new ConnectorsLicenseService(Observable.Empty<License>(), NullLogger<ConnectorsLicenseService>.Instance),
            _ => true,
            _ => true,
            time,
            new InMemoryEventStore()
        );
    }
}
