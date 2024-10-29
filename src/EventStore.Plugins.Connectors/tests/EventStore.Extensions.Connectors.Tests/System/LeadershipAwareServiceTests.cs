// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using System.Net;
using EventStore.Connectors.System;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Streaming;
using Microsoft.Extensions.Logging;
using Shouldly;
using MemberInfo = EventStore.Core.Cluster.MemberInfo;

namespace EventStore.Connectors.Tests.System;

[Trait("Category", "ControlPlane")]
[Trait("Category", "System")]
[Trait("Category", "Leadership")]
public class LeadershipAwareServiceTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
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

			GetNodeLifetimeService getNodeLifetimeService = component =>
				new NodeLifetimeService(
					component, MessageBus, MessageBus,
					Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
				);

			GetNodeSystemInfo getNodeSystemInfo = _ => ValueTask.FromResult(expected.NodeInfo);

			var sut = new TestLeadershipAwareService(serviceName, getNodeLifetimeService, getNodeSystemInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			var executed = await sut.WaitUntilExecute();

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

			var nodeLifetimeService = new NodeLifetimeService(
				serviceName, MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			GetNodeLifetimeService getNodeLifetimeService = _ => nodeLifetimeService;
			GetNodeSystemInfo      getNodeSystemInfo      = _ => ValueTask.FromResult(expected.NodeInfo);

			var sut = new TestLeadershipAwareService(serviceName, getNodeLifetimeService, getNodeSystemInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecute();

			MessageBus.Publish(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

			TaskCompletionSource componentTerminated = new();
			cancellator.Token.Register(() => componentTerminated.SetCanceled(cancellator.Token));
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, token) => {
				if (message.ComponentName == serviceName) {
					Fixture.Logger.LogInformation("Received component terminated message {Message}", message);
					componentTerminated.SetResult();
				}
			});

			await sut.StopAsync(cancellator.Token);

			await componentTerminated.Task;
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

			var nodeLifetimeService = new NodeLifetimeService(
				serviceName, MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			GetNodeLifetimeService getNodeLifetimeService = _ => nodeLifetimeService;
			GetNodeSystemInfo      getNodeSystemInfo      = _ => ValueTask.FromResult(expected.NodeInfo);

			var sut = new TestLeadershipAwareService(serviceName, getNodeLifetimeService, getNodeSystemInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			TaskCompletionSource componentTerminated = new();
			cancellator.Token.Register(() => componentTerminated.SetCanceled(cancellator.Token));
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, token) => {
				if (message.ComponentName == serviceName) {
					Fixture.Logger.LogInformation("Received component terminated message {Message}", message);
					componentTerminated.SetResult();
				}
			});

			// simulate waiting for leadership
			await Task.Delay(TimeSpan.FromSeconds(5), cancellator.Token);

			await sut.StopAsync(cancellator.Token);

			await componentTerminated.Task;
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

			var nodeLifetimeService = new NodeLifetimeService(
				serviceName, MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			GetNodeLifetimeService getNodeLifetimeService = _ => nodeLifetimeService;
			GetNodeSystemInfo      getNodeSystemInfo      = _ => ValueTask.FromResult(expected.NodeInfo);

			var sut = new TestLeadershipAwareService(serviceName, getNodeLifetimeService, getNodeSystemInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await sut.WaitUntilExecute(); // simulates work for 10m

			TaskCompletionSource componentTerminated = new();
			cancellator.Token.Register(() => componentTerminated.SetCanceled(cancellator.Token));
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, token) => {
				if (message.ComponentName == serviceName) {
					Fixture.Logger.LogInformation("Received component terminated message {Message}", message);
					componentTerminated.SetResult();
				}
			});

			await sut.StopAsync(cancellator.Token);

			await componentTerminated.Task;
		}
	);

	[Fact(Skip = "Because the type initializer for 'Humanizer.Configuration.Configurator' threw an exception.")]
	/* One day we will find out how to fix this... not really necessary to cover this one.
	 * System.TypeInitializationException: The type initializer for 'Humanizer.Configuration.Configurator' threw an exception.
	 * System.Globalization.CultureNotFoundException
	 *  Only the invariant culture is supported in globalization-invariant mode. See https://aka.ms/GlobalizationInvariantMode for more information. (Parameter 'name')
	 *  en-US is an invalid culture identifier.
	 */
	public Task stops_gracefully_when_stopping_token_is_cancelled() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(30), async cancellator => {
			// Arrange
			var serviceName = Fixture.NewIdentifier("test-svc");

			(NodeSystemInfo NodeInfo, CancellationToken StoppingToken) expected = (
				new NodeSystemInfo(new ClientClusterInfo.ClientMemberInfo(FakeMemberInfo), DateTimeOffset.UtcNow),
				cancellator.Token
			);

			var nodeLifetimeService = new NodeLifetimeService(
				serviceName, MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			GetNodeLifetimeService getNodeLifetimeService = _ => nodeLifetimeService;
			GetNodeSystemInfo      getNodeSystemInfo      = _ => ValueTask.FromResult(expected.NodeInfo);

			var sut = new TestLeadershipAwareService(serviceName, getNodeLifetimeService, getNodeSystemInfo, Fixture.LoggerFactory);

			await sut.StartAsync(cancellator.Token);

			// Act
			TaskCompletionSource componentTerminated = new();
			cancellator.Token.Register(() => componentTerminated.SetCanceled(cancellator.Token));
			MessageBus.Subscribe<SystemMessage.ComponentTerminated>((message, token) => {
				if (message.ComponentName == serviceName) {
					Fixture.Logger.LogInformation("Received component terminated message {Message}", message);
					componentTerminated.SetResult();
				}
			});

			await cancellator.CancelAsync();

			await componentTerminated.Task;
		}
	);
}

class TestLeadershipAwareService(string name, GetNodeLifetimeService getNodeLifetimeService, GetNodeSystemInfo getNodeSystemInfo, ILoggerFactory loggerFactory)
	: LeadershipAwareService(getNodeLifetimeService, getNodeSystemInfo, loggerFactory, name) {
	TaskCompletionSource<(NodeSystemInfo NodeInfo, CancellationToken StoppingToken)> CompletionSource { get; } = new();

	public TimeSpan ExecuteDuration { get; set; } = TimeSpan.FromMinutes(10);

	protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
		CompletionSource.SetResult((nodeInfo, stoppingToken));
		await Tasks.SafeDelay(ExecuteDuration, stoppingToken);
	}

	public Task<(NodeSystemInfo NodeInfo, CancellationToken StoppingToken)> WaitUntilExecute() => CompletionSource.Task;
}