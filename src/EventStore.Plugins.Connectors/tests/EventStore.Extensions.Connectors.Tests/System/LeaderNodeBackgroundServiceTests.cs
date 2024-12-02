// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using System.Net;
using EventStore.Connectors.System;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Toolkit;
using Microsoft.Extensions.Logging;
using Shouldly;
using MemberInfo = EventStore.Core.Cluster.MemberInfo;

namespace EventStore.Connectors.Tests.System;

[Trait("Category", "ControlPlane/Leadership")]
public class LeaderNodeBackgroundServiceTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	static readonly MessageBus MessageBus = new();

	static readonly MemberInfo FakeMemberInfo = MemberInfo.ForManager(Guid.NewGuid(), DateTime.Now, true, new IPEndPoint(0, 0));

	[Fact]
	public Task executes_when_leadership_assigned() => Fixture.TestWithTimeout(
		TimeSpan.FromMinutes(5), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			var executed = await sut.WaitUntilExecuting();

			// Assert
			executed.NodeInfo.ShouldBeEquivalentTo(expected.NodeInfo);
			executed.StoppingToken.IsCancellationRequested.Should().BeFalse();
		}
	);

	[Fact]
	public Task waits_for_leadership_again_when_leadership_revoked() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecuting();

			MessageBus.Publish(new SystemMessage.BecomeFollower(Guid.NewGuid(), FakeMemberInfo));

			TaskCompletionSource<SystemMessage.ComponentTerminated> componentTerminated = new();
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, _) => {
				if (message.ComponentName == serviceName)
					componentTerminated.SetResult(message);
			});

			await Task.Delay(1000, cancellator.Token);
			var executed = await sut.WaitUntilExecuted();

			// Assert
			executed.NodeInfo.ShouldBeEquivalentTo(expected.NodeInfo);
			executed.StoppingToken.IsCancellationRequested.Should().BeTrue();

			await sut.StopAsync(cancellator.Token);

			await componentTerminated.Task.Then(x => x.Should().BeEquivalentTo(new SystemMessage.ComponentTerminated(serviceName)));
		}
	);

	[Fact]
	public Task waits_for_leadership_again_when_leadership_revoked_multiple_times() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecuting();

			MessageBus.Publish(new SystemMessage.BecomeFollower(Guid.NewGuid(), FakeMemberInfo));

			await sut.WaitUntilExecuted();
			await Task.Delay(1000, cancellator.Token);

			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecuting();

			MessageBus.Publish(new SystemMessage.BecomeFollower(Guid.NewGuid(), FakeMemberInfo));

			await sut.WaitUntilExecuted();
			await Task.Delay(1000, cancellator.Token);

			TaskCompletionSource<SystemMessage.ComponentTerminated> componentTerminated = new();
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, _) => {
				if (message.ComponentName == serviceName)
					componentTerminated.SetResult(message);
			});

			await sut.StopAsync(cancellator.Token);

			await cancellator.CancelAsync();

			await componentTerminated.Task.Then(x => x.Should().BeEquivalentTo(new SystemMessage.ComponentTerminated(serviceName)));
		}
	);

	[Fact]
	public Task stops_gracefully_when_waiting_for_leadership_and_service_is_stopped() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			// simulate waiting for leadership
			await Task.Delay(TimeSpan.FromSeconds(5), cancellator.Token);

			await sut.StopAsync(cancellator.Token);
		}
	);

	[Fact]
	public Task stops_gracefully_when_leadership_assigned_and_service_is_stopped() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			TaskCompletionSource<SystemMessage.ComponentTerminated> componentTerminated = new();
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, _) => {
				if (message.ComponentName == serviceName)
					componentTerminated.SetResult(message);
			});

			await sut.WaitUntilExecuting();

			await sut.StopAsync(new CancellationToken(canceled: true));

			await sut.WaitUntilExecuted();

			await componentTerminated.Task.Then(x => x.Should().BeEquivalentTo(new SystemMessage.ComponentTerminated(serviceName)));
		}
	);

	[Fact]
	public Task stops_gracefully_when_waiting_for_leadership_and_stopping_token_is_cancelled() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			await cancellator.CancelAsync();
		}
	);

	[Fact]
	public Task stops_gracefully_when_leadership_assigned_and_stopping_token_is_cancelled() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecuting();

			TaskCompletionSource<SystemMessage.ComponentTerminated> componentTerminated = new();
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, _) => {
				if (message.ComponentName == serviceName)
					componentTerminated.SetResult(message);
			});

			await cancellator.CancelAsync();

			await sut.WaitUntilExecuted();

			await componentTerminated.Task.Then(x => x.Should().BeEquivalentTo(new SystemMessage.ComponentTerminated(serviceName)));
		}
	);

	[Fact]
	public Task stops_gracefully_when_leadership_revoked_and_waiting_for_leadership() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			using var sut = new TestLeadershipAwareService(serviceName, MessageBus, expected.NodeInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecuting();

			MessageBus.Publish(new SystemMessage.BecomeFollower(Guid.NewGuid(), FakeMemberInfo));

			await sut.WaitUntilExecuted();

			TaskCompletionSource<SystemMessage.ComponentTerminated> componentTerminated = new();
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, _) => {
				if (message.ComponentName == serviceName)
					componentTerminated.SetResult(message);
			});

			await sut.StopAsync(cancellator.Token);

			await componentTerminated.Task.Then(x => x.Should().BeEquivalentTo(new SystemMessage.ComponentTerminated(serviceName)));
		}
	);
}

class TestLeadershipAwareService(string serviceName, MessageBus bus, NodeSystemInfo nodeSystemInfo, ILoggerFactory loggerFactory)
	: LeaderNodeBackgroundService(bus, bus, _ => new ValueTask<NodeSystemInfo>(nodeSystemInfo), loggerFactory, serviceName) {
	volatile TaskCompletionSource<(NodeSystemInfo NodeInfo, CancellationToken StoppingToken)> _executingCompletionSource = new();
	volatile TaskCompletionSource<(NodeSystemInfo NodeInfo, CancellationToken StoppingToken)> _executedCompletionSource  = new();

	public TimeSpan ExecuteDelay { get; set; } = TimeSpan.FromMinutes(10);

	protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
		_executingCompletionSource.SetResult((nodeInfo, stoppingToken));
		await Tasks.SafeDelay(ExecuteDelay, stoppingToken);
		_executedCompletionSource.SetResult((nodeInfo, stoppingToken));
	}

	public async Task<(NodeSystemInfo NodeInfo, CancellationToken StoppingToken)> WaitUntilExecuting() {
		var result = await _executingCompletionSource.Task;
		_executingCompletionSource = new();
		return result;
	}

	public async Task<(NodeSystemInfo NodeInfo, CancellationToken StoppingToken)> WaitUntilExecuted() {
		var result = await _executedCompletionSource.Task;
		_executedCompletionSource = new();
		return result;
	}
}