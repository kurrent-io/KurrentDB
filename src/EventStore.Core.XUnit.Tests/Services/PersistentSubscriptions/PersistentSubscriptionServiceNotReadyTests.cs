// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.LogV2;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Services;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Core.Tests.TransactionLog;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.PersistentSubscriptions;

public class PersistentSubscriptionServiceNotReadyTests {
	private readonly FakePublisher _publisher = new();
	private readonly IODispatcher _ioDispatcher;

	public PersistentSubscriptionServiceNotReadyTests() {
		var bus = new SynchronousScheduler();
		_ioDispatcher = new IODispatcher(_publisher, bus);
		bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
	}

	private PersistentSubscriptionService<string> CreateSut() {
		_publisher.Messages.Clear();
		var subscriber = new SynchronousScheduler();
		var queuedHandler = new QueuedHandlerThreadPool(subscriber, "test", new QueueStatsManager(), new QueueTrackers());
		var index = new FakeReadIndex<LogFormat.V2, string>(_ => false, new LogV2SystemStreams());
		var strategyRegistry = new PersistentSubscriptionConsumerStrategyRegistry(_publisher, subscriber,
			Array.Empty<IPersistentSubscriptionConsumerStrategyFactory>());
		return new PersistentSubscriptionService<string>(
			queuedHandler, index, _ioDispatcher, _publisher, strategyRegistry, IPersistentSubscriptionTracker.NoOp);
	}

	private static void AssertNotHandledNotReady(FakeEnvelope envelope) {
		var reply = Assert.Single(envelope.Replies);
		var notHandled = Assert.IsType<ClientMessage.NotHandled>(reply);
		Assert.Equal(ClientMessage.NotHandled.Types.NotHandledReason.NotReady, notHandled.Reason);
	}

	[Fact]
	public async Task connect_to_stream_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();
		var correlationId = Guid.NewGuid();

		var message = new ClientMessage.ConnectToPersistentSubscriptionToStream(
			Guid.NewGuid(), correlationId, envelope,
			Guid.NewGuid(), "connection", "group", "stream",
			10, string.Empty, null);

		await ((IAsyncHandle<ClientMessage.ConnectToPersistentSubscriptionToStream>)sut)
			.HandleAsync(message, CancellationToken.None);

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public async Task connect_to_all_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();
		var correlationId = Guid.NewGuid();

		var message = new ClientMessage.ConnectToPersistentSubscriptionToAll(
			Guid.NewGuid(), correlationId, envelope,
			Guid.NewGuid(), "connection", "group",
			10, string.Empty, null);

		await ((IAsyncHandle<ClientMessage.ConnectToPersistentSubscriptionToAll>)sut)
			.HandleAsync(message, CancellationToken.None);

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void create_stream_subscription_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.CreatePersistentSubscriptionToStream(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"stream", "group", resolveLinkTos: false, startFrom: 0,
			messageTimeoutMilliseconds: 10000, recordStatistics: false,
			maxRetryCount: 5, bufferSize: 10, liveBufferSize: 10,
			readbatchSize: 10, checkPointAfterMilliseconds: 1000,
			minCheckPointCount: 1, maxCheckPointCount: 1,
			maxSubscriberCount: 10, namedConsumerStrategy: SystemConsumerStrategies.RoundRobin,
			user: null));

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void create_all_subscription_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.CreatePersistentSubscriptionToAll(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"group", eventFilter: null, resolveLinkTos: false,
			startFrom: new TFPos(0, 0),
			messageTimeoutMilliseconds: 10000, recordStatistics: false,
			maxRetryCount: 5, bufferSize: 10, liveBufferSize: 10,
			readbatchSize: 10, checkPointAfterMilliseconds: 1000,
			minCheckPointCount: 1, maxCheckPointCount: 1,
			maxSubscriberCount: 10, namedConsumerStrategy: SystemConsumerStrategies.RoundRobin,
			user: null));

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void update_stream_subscription_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.UpdatePersistentSubscriptionToStream(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"stream", "group", resolveLinkTos: false, startFrom: 0,
			messageTimeoutMilliseconds: 10000, recordStatistics: false,
			maxRetryCount: 5, bufferSize: 10, liveBufferSize: 10,
			readbatchSize: 10, checkPointAfterMilliseconds: 1000,
			minCheckPointCount: 1, maxCheckPointCount: 1,
			maxSubscriberCount: 10, namedConsumerStrategy: SystemConsumerStrategies.RoundRobin,
			user: null));

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void update_all_subscription_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.UpdatePersistentSubscriptionToAll(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"group", resolveLinkTos: false,
			startFrom: new TFPos(0, 0),
			messageTimeoutMilliseconds: 10000, recordStatistics: false,
			maxRetryCount: 5, bufferSize: 10, liveBufferSize: 10,
			readbatchSize: 10, checkPointAfterMilliseconds: 1000,
			minCheckPointCount: 1, maxCheckPointCount: 1,
			maxSubscriberCount: 10, namedConsumerStrategy: SystemConsumerStrategies.RoundRobin,
			user: null));

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void delete_stream_subscription_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.DeletePersistentSubscriptionToStream(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"stream", "group", user: null));

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void delete_all_subscription_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.DeletePersistentSubscriptionToAll(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"group", user: null));

		AssertNotHandledNotReady(envelope);
	}

	[Fact]
	public void read_next_messages_replies_not_handled_when_not_started() {
		var sut = CreateSut();
		var envelope = new FakeEnvelope();

		sut.Handle(new ClientMessage.ReadNextNPersistentMessages(
			Guid.NewGuid(), Guid.NewGuid(), envelope,
			"stream", "group", 10, user: null));

		AssertNotHandledNotReady(envelope);
	}
}
