// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Integration;
using EventStore.Core.Tests.Services.Replication;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_restarting_with_a_connected_subscription<TLogFormat, TStreamId> : specification_with_a_single_node<TLogFormat, TStreamId> {
	private readonly ManualResetEvent _subscriptionDropped = new ManualResetEvent(false);
	private readonly ManualResetEvent _serviceStarted = new ManualResetEvent(false);
	private readonly ManualResetEvent _serviceStopped = new ManualResetEvent(false);

	protected override Task Given() {
		var testUser = new ClaimsPrincipal(new ClaimsIdentity(
			new[] {
				new Claim(ClaimTypes.Name, "admin"),
			}, "ES-Test"));

		_node.Node.MainBus.Subscribe(
			new AdHocHandler<SubscriptionMessage.PersistentSubscriptionsStarted>(_ => _serviceStarted.Set()));

		_node.Node.MainBus.Subscribe(
			new AdHocHandler<SubscriptionMessage.PersistentSubscriptionsStopped>(_ => _serviceStopped.Set()));

		var streamId = Guid.NewGuid().ToString();
		var group = Guid.NewGuid().ToString();
		_node.Node.MainQueue.Handle(new ClientMessage.CreatePersistentSubscriptionToStream(Guid.NewGuid(), Guid.NewGuid(),
			new FakeEnvelope(), streamId, group, false, 0, 0, false, 0, 20, 20, 10, 1, 1, 10, 1,
			"RoundRobin",
			testUser, DateTime.UtcNow));

		_node.Node.MainQueue.Handle(new ClientMessage.ConnectToPersistentSubscriptionToStream(Guid.NewGuid(),
			Guid.NewGuid(), new CallbackEnvelope(message => {
				_subscriptionDropped.Set();
			}), Guid.NewGuid(), Guid.NewGuid().ToString(), group, streamId, 1, "0",
			testUser));

		_node.Node.MainQueue.Handle(
			new SubscriptionMessage.PersistentSubscriptionsRestart(new FakeEnvelope()));
		return base.Given();
	}

	[Test]
	public void should_drop_subscription() {
		Assert.IsTrue(_subscriptionDropped.WaitOne(TimeSpan.FromSeconds(5)));
	}

	[Test]
	public void should_have_stopped_the_service() {
		Assert.IsTrue(_serviceStopped.WaitOne(TimeSpan.FromSeconds(5)));
	}

	[Test]
	public void should_have_started_the_service() {
		Assert.IsTrue(_serviceStarted.WaitOne(TimeSpan.FromSeconds(5)));
	}
}
