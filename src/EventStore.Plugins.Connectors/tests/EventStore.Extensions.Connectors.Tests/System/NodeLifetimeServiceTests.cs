// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using System.Net;
using EventStore.Connectors.System;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Streaming;
using EventStore.Toolkit.Testing.Xunit;
using Microsoft.Extensions.Logging;
using MemberInfo = EventStore.Core.Cluster.MemberInfo;

namespace EventStore.Connectors.Tests.System;

[Trait("Category", "System")]
public class NodeLifetimeServiceTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	static readonly MessageBus MessageBus = new();

	static readonly MemberInfo FakeMemberInfo = MemberInfo.ForManager(Guid.NewGuid(), DateTime.Now, true, new IPEndPoint(0, 0));

	[Fact]
	public Task returns_token_when_leadership_assigned() => Fixture.TestWithTimeout(
		async cancellator => {
			// Arrange
			using var sut = new NodeLifetimeService(
				Fixture.NewIdentifier("node-lifetime"),
				MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			var task   = sut.WaitForLeadershipAsync(cancellator.Token);
			var action = () => task;

			// Act
			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			// Assert
			await action.Should().NotThrowAsync()
				.Then(x => x.Which.IsCancellationRequested.Should().BeFalse());
		}
	);

	public class LeadershipRevokedTestCases : TestCaseGenerator<LeadershipRevokedTestCases> {
		protected override IEnumerable<object[]> Data() =>
			Enum.GetValues(typeof(VNodeState))
				.Cast<VNodeState>()
				.Where(state => state != VNodeState.Leader)
				.Select(state => (object[]) [new NodeStateChanged(state)]);
	}

	[Theory, LeadershipRevokedTestCases]
	public Task cancels_token_when_leadership_revoked(SystemMessage.StateChangeMessage stateChanged) => Fixture.TestWithTimeout(
		async cancellator => {
			using var sut = new NodeLifetimeService(
				Fixture.NewIdentifier("node-lifetime"),
				MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			var task   = sut.WaitForLeadershipAsync(cancellator.Token);
			var action = () => task;

			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			// Act
			MessageBus.Publish(stateChanged);

			await action.Should().NotThrowAsync()
				.Then(x => x.Which.IsCancellationRequested.Should().BeTrue());
		}
	);

	[Fact]
	public Task returns_cancelled_token_when_service_shutting_down_or_disposed() => Fixture.TestWithTimeout(
		async cancellator => {
			var sut = new NodeLifetimeService(
				Fixture.NewIdentifier("node-lifetime"),
				MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			cancellator.CancelAfter(TimeSpan.FromSeconds(3));

			var task = await sut.WaitForLeadershipAsync(cancellator.Token);

			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			// Act
			sut.Dispose();

			// this message will be ignored and that is expected
			MessageBus.Publish(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

			var action = () => sut.WaitForLeadershipAsync(cancellator.Token);
			await action.Should().NotThrowAsync()
				.Then(x => x.Which.IsCancellationRequested.Should().BeTrue());
		}
	);

	[Fact]
	public Task returns_token_when_leadership_reassigned() => Fixture.TestWithTimeout(
		async cancellator => {
			// Arrange
			using var sut = new NodeLifetimeService(
				Fixture.NewIdentifier("node-lifetime"),
				MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			var waitForLeadershipTask = sut.WaitForLeadershipAsync(cancellator.Token);

			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			await waitForLeadershipTask;

			MessageBus.Publish(new SystemMessage.BecomeFollower(Guid.NewGuid(), FakeMemberInfo));

			// Act & Assert

			// wait for leadership again
			waitForLeadershipTask = sut.WaitForLeadershipAsync(cancellator.Token);

			MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

			var action = () => waitForLeadershipTask;
			await action.Should().NotThrowAsync()
				.Then(x => x.Which.IsCancellationRequested.Should().BeFalse());
		}
	);

	[Fact]
	public Task returns_cancelled_token_when_stopping_token_is_cancelled_while_waiting() => Fixture.TestWithTimeout(
		TimeSpan.FromSeconds(3), async cancellator => {
			// Arrange
			using var sut = new NodeLifetimeService(
				Fixture.NewIdentifier("node-lifetime"),
				MessageBus, MessageBus,
				Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>()
			);

			var action = () => sut.WaitForLeadershipAsync(cancellator.Token);

			// Act & Assert
			await action.Should().NotThrowAsync()
				.Then(x => x.Which.IsCancellationRequested.Should().BeTrue());
		}
	);

	[Fact]
	public async Task returns_cancelled_token_when_stopping_token_was_already_cancelled() {
		// Arrange
		using var sut = new NodeLifetimeService(Fixture.NewIdentifier("node-lifetime"),
			MessageBus,
			MessageBus,
			Fixture.LoggerFactory.CreateLogger<NodeLifetimeService>());

		var action = () => sut.WaitForLeadershipAsync(new CancellationToken(true));

		// Act & Assert
		await action.Should().NotThrowAsync()
			.Then(x => x.Which.IsCancellationRequested.Should().BeTrue());
	}
}

class NodeStateChanged(VNodeState state = VNodeState.Unknown) : SystemMessage.StateChangeMessage(Guid.NewGuid(), state);