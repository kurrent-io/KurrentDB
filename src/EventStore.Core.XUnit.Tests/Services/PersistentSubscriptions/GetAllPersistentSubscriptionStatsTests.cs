// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
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
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;
using OperationStatus = EventStore.Core.Messages.MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus;

namespace EventStore.Core.XUnit.Tests.Services.PersistentSubscriptions;

public class GetAllPersistentSubscriptionStatsTests {
	private readonly FakePublisher _publisher = new();
	private readonly FakeTimeProvider _timeProvider = new ();
	private readonly IODispatcher _ioDispatcher;

	public GetAllPersistentSubscriptionStatsTests() {
		var bus = new SynchronousScheduler();
		_ioDispatcher = new IODispatcher(_publisher, bus);
		bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
	}

	private PersistentSubscriptionService<string> CreateSut() {
		_publisher.Messages.Clear();
		var subscriber = new SynchronousScheduler();
		var queuedHandler =
			new QueuedHandlerThreadPool(subscriber, "test", new QueueStatsManager(), new QueueTrackers());
		var index = new FakeReadIndex<LogFormat.V2, string>(_ => false, new LogV2SystemStreams());
		var strategyRegistry = new PersistentSubscriptionConsumerStrategyRegistry(_publisher, subscriber,
			Array.Empty<IPersistentSubscriptionConsumerStrategyFactory>());
		return new PersistentSubscriptionService<string>(
			queuedHandler, index, _ioDispatcher, _publisher, strategyRegistry, IPersistentSubscriptionTracker.NoOp);
	}

	[Fact]
	public void when_not_ready() {
		var responseEnvelope = new FakeEnvelope();
		var sut = CreateSut();

		sut.Handle(new MonitoringMessage.GetAllPersistentSubscriptionStats(responseEnvelope));
		Assert.Single(responseEnvelope.Replies);
		var response =
			responseEnvelope.Replies.FirstOrDefault() as MonitoringMessage.GetPersistentSubscriptionStatsCompleted;
		Assert.NotNull(response);
		Assert.Equal(OperationStatus.NotReady, response.Result);
		Assert.Equal(0, response.Total);
	}

	[Fact]
	public void when_there_are_no_persistent_subscriptions() {
		// Arrange
		var responseEnvelope = new FakeEnvelope();
		var sut = CreateSut();
		sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		// Get the read request and give an empty response
		var read = _publisher.Messages.OfType<ClientMessage.ReadStreamEventsBackward>().FirstOrDefault();
		Assert.NotNull(read);
		read.Envelope.ReplyWith(new ClientMessage.ReadStreamEventsBackwardCompleted(read.CorrelationId,
			read.EventStreamId, read.FromEventNumber, read.MaxCount, ReadStreamResult.NoStream, [],
			new StreamMetadata(), true, "", -1, -1, true, 1000));

		// Act
		sut.Handle(new MonitoringMessage.GetAllPersistentSubscriptionStats(responseEnvelope));
		var response = responseEnvelope.Replies.OfType<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>()
			.FirstOrDefault();

		// Assert
		Assert.NotNull(response);
		Assert.Equal(OperationStatus.Success, response.Result);
		Assert.Empty(response.SubscriptionStats);
		Assert.Equal(0, response.Total);
	}

	[Fact]
	public void when_a_persistent_subscription_exists() {
		// Arrange
		var responseEnvelope = new FakeEnvelope();
		var sut = CreateSut();
		var psubEntry = new PersistentSubscriptionEntry{
			Group = "test-group",
			Stream = "test-stream",
			HistoryBufferSize = 20,
			LiveBufferSize = 20,
			ReadBatchSize = 10,
			NamedConsumerStrategy = SystemConsumerStrategies.RoundRobin
		};
		var psubConfig = new PersistentSubscriptionConfig {
			Version = "2",
			Updated = _timeProvider.UtcNow,
			UpdatedBy = "admin",
			Entries = [psubEntry]
		};

		sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		// Get the read request and respond with the config
		var read = _publisher.Messages.OfType<ClientMessage.ReadStreamEventsBackward>().FirstOrDefault();
		Assert.NotNull(read);
		read.Envelope.ReplyWith(ReadPersistentSubscriptionConfigCompleted(read, psubConfig));

		// Act
		sut.Handle(new MonitoringMessage.GetAllPersistentSubscriptionStats(responseEnvelope));
		var response = responseEnvelope.Replies.OfType<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>()
			.FirstOrDefault();

		// Assert
		Assert.NotNull(response);
		Assert.Equal(OperationStatus.Success, response.Result);
		Assert.Single(response.SubscriptionStats);
		Assert.Equal(0, response.RequestedOffset);
		Assert.Equal(1, response.Total);
		var responseEntry = response.SubscriptionStats[0];
		Assert.Equal(psubEntry.Group, responseEntry.GroupName);
		Assert.Equal(psubEntry.Stream, responseEntry.EventSource);
		Assert.Equal(psubEntry.NamedConsumerStrategy, responseEntry.NamedConsumerStrategy);
		Assert.Empty(responseEntry.Connections);
	}

	[Theory]
	[InlineData(0, 0, new string[] { })]
	[InlineData(10, 0, new string[] { })]
	[InlineData(0, 2, new[] { "stream-1", "stream-2" })]
	[InlineData(2, 2, new[] { "stream-3", "stream-4" })]
	[InlineData(3, 2, new[] { "stream-4" })]
	[InlineData(4, 2, new string[] { })]
	public void when_getting_all_stats_from_multiple_subscriptions_paged(int offset, int count, string[] expectedTopics) {
		// Arrange
		var responseEnvelope = new FakeEnvelope();
		var sut = CreateSut();
		var psubs = new List<PersistentSubscriptionEntry>();
		var topics = new int[] { 3, 2, 0, 1 }; // no particular order
		foreach (var topic in topics) {
			for (var group = 0; group < 2; group++) {
				psubs.Add(new PersistentSubscriptionEntry {
					Group = $"{group + 1}",
					Stream = $"stream-{topic + 1}",
					HistoryBufferSize = 20,
					LiveBufferSize = 20,
					ReadBatchSize = 10,
					NamedConsumerStrategy = SystemConsumerStrategies.RoundRobin
				});
			}
		}

		var psubConfig = new PersistentSubscriptionConfig {
			Version = "2",
			Updated = _timeProvider.UtcNow,
			UpdatedBy = "admin",
			Entries = psubs
		};

		sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		// Get the read request and respond with the config
		var read = _publisher.Messages.OfType<ClientMessage.ReadStreamEventsBackward>().FirstOrDefault();
		Assert.NotNull(read);
		read.Envelope.ReplyWith(ReadPersistentSubscriptionConfigCompleted(read, psubConfig));

		// Act
		sut.Handle(new MonitoringMessage.GetAllPersistentSubscriptionStats(responseEnvelope, offset, count));
		var response = responseEnvelope.Replies.OfType<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>()
			.SingleOrDefault();

		// Assert
		Assert.NotNull(response);
		Assert.Equal(OperationStatus.Success, response.Result);
		Assert.Equal(offset, response.RequestedOffset);
		Assert.Equal(4, response.Total);

		var actualTopics = response.SubscriptionStats
			.GroupBy(x => x.EventSource)
			.Select(x => x.Key)
			.Order();
		Assert.Equal(expectedTopics, actualTopics);
	}

	private ClientMessage.ReadStreamEventsBackwardCompleted ReadPersistentSubscriptionConfigCompleted(
		ClientMessage.ReadStreamEventsBackward message, PersistentSubscriptionConfig config) {
		var streamId = SystemStreams.PersistentSubscriptionConfig;
		var eventType = SystemEventTypes.PersistentSubscriptionConfig;
		var eventData = config.GetSerializedForm();

		var record = new EventRecord(0, LogRecord.Prepare(
				LogFormatHelper<LogFormat.V2, string>.RecordFactory,
				100, Guid.NewGuid(), Guid.NewGuid(), 100, 0, streamId, -1,
				PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd | PrepareFlags.IsJson,
				eventType, eventData, Array.Empty<byte>(), _timeProvider.UtcNow), streamId, eventType);

		return new ClientMessage.ReadStreamEventsBackwardCompleted(message.CorrelationId, message.EventStreamId,
			message.FromEventNumber, message.MaxCount, ReadStreamResult.Success,
			[ResolvedEvent.ForUnresolvedEvent(record, 100)], new StreamMetadata(),
			true, "", 1, 0, true, 1000);
	}
}
