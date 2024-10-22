// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Net;
using EventStore.Connectors.System;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Extensions.Connectors.Tests;
using EventStore.System.Testing.Fixtures;
using EventStore.Toolkit.Testing.Fixtures;

namespace EventStore.Connectors.Tests.Management;

public class NodeLifetimeServiceTests(ITestOutputHelper output, NodeLifetimeFixture fixture)
    : FastTests<NodeLifetimeFixture>(output, fixture) {
    static MemberInfo BogusMemberInfo { get; } =
        MemberInfo.Initial(Guid.NewGuid(),
            DateTime.Now,
            VNodeState.Leader,
            true,
            new IPEndPoint(0, 0),
            new IPEndPoint(0, 0),
            new IPEndPoint(0, 0),
            new IPEndPoint(0, 0),
            new IPEndPoint(0, 0),
            "",
            0,
            1,
            2,
            false);

    [Fact]
    public void from_leader_to_follower() {
        var sut = new NodeLifetimeService(Fixture.NewIdentifier(), new FakePublisher());

        var task = sut.WaitForLeadershipAsync(Timeout.InfiniteTimeSpan);
        task.IsCompleted.Should().BeFalse();
        sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
        task.IsCompleted.Should().BeTrue();
        sut.Handle(new SystemMessage.BecomeFollower(Guid.NewGuid(), BogusMemberInfo));
        task = sut.WaitForLeadershipAsync(Timeout.InfiniteTimeSpan);
        task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void from_follower_to_leader() {
        var sut = new NodeLifetimeService(Fixture.NewIdentifier(), new FakePublisher());

        var task = sut.WaitForLeadershipAsync(Timeout.InfiniteTimeSpan);
        task.IsCompleted.Should().BeFalse();
        sut.Handle(new SystemMessage.BecomeFollower(Guid.NewGuid(), BogusMemberInfo));
        task.IsCompleted.Should().BeFalse();
        sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
        task.IsCompleted.Should().BeTrue();
    }

    [Trait("Category", "Shutdown")]
    public class GracefulShutdownTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture)
        : ClusterVNodeTests<ConnectorsAssemblyFixture>(output, fixture) {

        [Fact]
        public async Task graceful_shutdown_working() {
            var       completionSources = new List<Task>();
            var       suts              = new List<NodeLifetimeService>();
            const int count             = 3;

            for (var i = 0; i < count; i++) {
                var source = new TaskCompletionSource();
                var sut = new NodeLifetimeService(Fixture.NewIdentifier(), Fixture.Publisher);

                sut.ShutdownInitiated += () => source.TrySetResult();
                completionSources.Add(source.Task);
                suts.Add(sut);
            }

            foreach (var sut in suts) {
                var task = sut.WaitForLeadershipAsync(Timeout.InfiniteTimeSpan);
                task.IsCompleted.Should().BeFalse();
                sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
                task.IsCompleted.Should().BeTrue();
            }

            Fixture.Publisher.Publish(new ClientMessage.RequestShutdown(true, true));

            await Task.WhenAll(completionSources);

            foreach (var sut in suts) {
                await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    sut.WaitForLeadershipAsync(Timeout.InfiniteTimeSpan));
            }
        }
    }
}