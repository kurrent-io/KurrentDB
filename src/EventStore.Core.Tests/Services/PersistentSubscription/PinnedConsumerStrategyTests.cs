// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Core.Tests.Services.Storage;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture]
public class PinnedConsumerStrategyTests {
	[Test]
	public void live_subscription_with_pinned_pushes_events_to_same_client_for_stream_id() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();

		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor("$ce-streamName", "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 1));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 2));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 3));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 4));

		Assert.AreEqual(3, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-3", 5));

		Assert.AreEqual(4, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);
	}

	[Test]
	public void new_stream_is_round_robinned_to_next_consumer() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor("$ce-streamName", "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 1));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-3", 2));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-4", 4));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);
	}

	[Test]
	public void resolved_link_events_use_event_stream_id() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0)));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0)));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 2,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-3", 0)));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 3,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-4", 0)));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);
	}

	[Test]
	public void link_events_use_data_to_detect_stream_id() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0), false));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 2,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-3", 0), false));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 3,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-4", 0), false));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);
	}

	[Test]
	public void unsubscribed_client_has_assigned_streams_sent_to_another_client() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		var client2Id = Guid.NewGuid();
		sub.AddClient(client2Id, Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0), false));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.RemoveClientByCorrelationId(client2Id, false);

		// Message 2 should be retried on client 1 as it wasn't acked.
		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);
	}

	[Test]
	public void new_subscription_gets_work() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var client3Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new ByLengthHasher()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "1", 0));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "11", 1));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "111", 2));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "1111", 3));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "11111", 4));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "111111", 5));

		Assert.AreEqual(3, client1Envelope.Replies.Count);
		Assert.AreEqual(3, client2Envelope.Replies.Count);

		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-3", client3Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "1", 6));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "11", 7));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "111", 8));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "1111", 9));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "11111", 10));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "111111", 11));

		Assert.AreEqual(5, client1Envelope.Replies.Count);
		Assert.AreEqual(5, client2Envelope.Replies.Count);
		Assert.AreEqual(2, client3Envelope.Replies.Count);
	}

	[Test]
	public void bad_availablecapacity_scneanrio_causing_null_reference() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new ByLengthHasher()))
				.StartFromCurrent());
		reader.Load(null);
		var conn1Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), conn1Id, "connection-1", client1Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "1", 0));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "11", 1));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEvent(Guid.NewGuid(), "type", "111", 2));

		Assert.AreEqual(3, client1Envelope.Replies.Count);

		var conn2Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), conn2Id, "connection-2", client2Envelope, 10, "foo", "bar");

		Assert.AreEqual(3, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		Assert.IsTrue(sub.RemoveClientByConnectionId(conn1Id));

		// Used to throw a null reference exception.
		Assert.That(() => sub.RemoveClientByConnectionId(conn2Id), Throws.Nothing);
	}

	[Test]
	public void disconnected_client_has_assigned_streams_sent_to_another_client() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		var client2Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), client2Id, "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0), false));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		Assert.IsTrue(sub.RemoveClientByConnectionId(client2Id));

		// Message 2 should be retried on client 1 as it wasn't acked.
		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);
	}

	[Test]
	public void available_capacity_is_tracked() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		PersistentSubscriptionParams settings = PersistentSubscriptionToStreamParamsBuilder
			.CreateFor(subsctiptionStream, "groupName")
			.WithEventLoader(new FakeStreamReader())
			.WithCheckpointReader(reader)
			.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
			.WithMessageParker(new FakeMessageParker())
			.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
			.StartFromCurrent();
		var consumerStrategy = (PinnedPersistentSubscriptionConsumerStrategy)settings.ConsumerStrategy;
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(settings);
		reader.Load(null);
		var client1Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), client1Id, "connection-1", client1Envelope, 14, "foo", "bar");

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(14));

		var client2Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), client2Id, "connection-2", client2Envelope, 10, "foo", "bar");

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(24));

		Assert.IsTrue(sub.RemoveClientByConnectionId(client2Id));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(14));

		Assert.IsTrue(sub.RemoveClientByConnectionId(client1Id));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(0));
	}

	[Test]
	public void available_capacity_is_tracked_with_inflight_messages() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		PersistentSubscriptionParams settings = PersistentSubscriptionToStreamParamsBuilder
			.CreateFor(subsctiptionStream, "groupName")
			.WithEventLoader(new FakeStreamReader())
			.WithCheckpointReader(reader)
			.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
			.WithMessageParker(new FakeMessageParker())
			.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
			.StartFromCurrent();
		var consumerStrategy = (PinnedPersistentSubscriptionConsumerStrategy)settings.ConsumerStrategy;
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(settings);
		reader.Load(null);
		var client1Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), client1Id, "connection-1", client1Envelope, 14, "foo", "bar");
		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(14));

		var client2Id = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), client2Id, "connection-2", client2Envelope, 10, "foo", "bar");

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(24));

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(23));

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0), false));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(22));

		Assert.IsTrue(sub.RemoveClientByConnectionId(client2Id));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(12));

		Assert.IsTrue(sub.RemoveClientByConnectionId(client1Id));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(0));
	}

	[Test]
	public void available_capacity_is_tracked_with_acked_messages() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		PersistentSubscriptionParams settings = PersistentSubscriptionToStreamParamsBuilder
			.CreateFor(subsctiptionStream, "groupName")
			.WithEventLoader(new FakeStreamReader())
			.WithCheckpointReader(reader)
			.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
			.WithMessageParker(new FakeMessageParker())
			.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
			.StartFromCurrent();
		var consumerStrategy = (PinnedPersistentSubscriptionConsumerStrategy)settings.ConsumerStrategy;
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(settings);
		reader.Load(null);
		var correlationId1 = Guid.NewGuid();
		sub.AddClient(correlationId1, Guid.NewGuid(), "connection-1", client1Envelope, 14, "foo", "bar");
		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(14));

		var correlationId2 = Guid.NewGuid();
		sub.AddClient(correlationId2, Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(24));

		var message1 = Guid.NewGuid();
		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(message1, subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(23));

		var message2 = Guid.NewGuid();
		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(message2, subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0), false));

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(22));

		sub.AcknowledgeMessagesProcessed(correlationId1, new[] { message1 });

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(23));

		sub.AcknowledgeMessagesProcessed(correlationId2, new[] { message2 });

		Assert.That(consumerStrategy.AvailableCapacity, Is.EqualTo(24));
	}

	[Test]
	public void events_are_skipped_if_assigned_client_full() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		const string subsctiptionStream = "$ce-streamName";
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor(subsctiptionStream, "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());
		reader.Load(null);

		var correlationId = Guid.NewGuid();
		sub.AddClient(correlationId, Guid.NewGuid(), "connection-1", client1Envelope, 1, "foo", "bar");

		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 1, "foo", "bar");


		var message1 = Guid.NewGuid();
		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(message1, subsctiptionStream, 0,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));

		Assert.That(client1Envelope.Replies.Count, Is.EqualTo(1));
		Assert.That(client2Envelope.Replies.Count, Is.EqualTo(0));

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 1,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-1", 0), false));

		Assert.That(client1Envelope.Replies.Count, Is.EqualTo(1));
		Assert.That(client2Envelope.Replies.Count, Is.EqualTo(0));
		Assert.True(sub.TryGetStreamBuffer(out var streamBuffer));
		Assert.That(streamBuffer.BufferCount, Is.EqualTo(1));

		sub.NotifyLiveSubscriptionMessage(Helper.BuildLinkEvent(Guid.NewGuid(), subsctiptionStream, 2,
			Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamName-2", 0), false));

		Assert.That(client1Envelope.Replies.Count, Is.EqualTo(1));
		Assert.That(client2Envelope.Replies.Count, Is.EqualTo(1));

		Assert.That(streamBuffer.BufferCount, Is.EqualTo(1));

		sub.AcknowledgeMessagesProcessed(correlationId, new[] { message1 });

		Assert.That(client1Envelope.Replies.Count, Is.EqualTo(2));
		Assert.That(client2Envelope.Replies.Count, Is.EqualTo(1));

		Assert.That(streamBuffer.BufferCount, Is.EqualTo(0));
	}
}
