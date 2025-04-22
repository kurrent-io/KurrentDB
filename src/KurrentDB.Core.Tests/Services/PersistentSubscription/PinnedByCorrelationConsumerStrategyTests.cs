// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.PersistentSubscription;
using KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;
using KurrentDB.Core.Tests.Services.Replication;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.PersistentSubscription;

[TestFixture]
public class PinnedByCorrelationConsumerStrategyTests {
	[Test]
	public void live_subscription_with_pinned_by_correlation_pushes_events_without_metadata_to_same_client_for_stream_id() {
		live_subscription_with_pinned_by_correlation_pushes_events_to_same_client_for_stream_id_for_metadata(new byte[0]);

	}

	[Test]
	public void live_subscription_with_pinned_by_correlation_pushes_events_with_empty_metadata_to_same_client_for_stream_id() {
		live_subscription_with_pinned_by_correlation_pushes_events_to_same_client_for_stream_id_for_metadata(Encoding.UTF8.GetBytes("{}"));
	}

	[Test]
	public void live_subscription_with_pinned_by_correlation_pushes_events_with_metadata_without_closing_brace_to_same_client_for_stream_id() {
		live_subscription_with_pinned_by_correlation_pushes_events_to_same_client_for_stream_id_for_metadata(Encoding.UTF8.GetBytes("{"));
	}

	[Test]
	public void live_subscription_with_pinned_by_correlation_pushes_events_with_metadata_without_correlation_to_same_client_for_stream_id() {
		live_subscription_with_pinned_by_correlation_pushes_events_to_same_client_for_stream_id_for_metadata(Encoding.UTF8.GetBytes(@"{ ""x"": ""x"" }"));
	}


	[Test]
	public void live_subscription_with_pinned_by_correlation_pushes_events_with_metadata_with_invalid_correlation_to_same_client_for_stream_id() {
		live_subscription_with_pinned_by_correlation_pushes_events_to_same_client_for_stream_id_for_metadata(Encoding.UTF8.GetBytes(@"{ ""$correlationId"": 0 }"));
	}

	private static void
		live_subscription_with_pinned_by_correlation_pushes_events_to_same_client_for_stream_id_for_metadata(byte[] metaData) {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();

		var sub = new KurrentDB.Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor("$ce-streamName", "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedByCorrelationPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());

		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		sub.NotifyLiveSubscriptionMessage(
			Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-1", 0, metaData));
		sub.NotifyLiveSubscriptionMessage(
			Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-1", 1, metaData));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(
			Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-2", 2, metaData));
		sub.NotifyLiveSubscriptionMessage(
			Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-2", 3, metaData));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(
			Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-1", 4, metaData));

		Assert.AreEqual(3, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(
			Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-3", 5, metaData));

		Assert.AreEqual(4, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);
	}

	[Test]
	public void live_subscription_with_pinned_by_correlation_pushes_events_with_metadata_with_valid_correlation_to_same_client_for_correlation_id() {

		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();

		var sub = new KurrentDB.Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToStreamParamsBuilder.CreateFor("$ce-streamName", "groupName")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.CustomConsumerStrategy(new PinnedByCorrelationPersistentSubscriptionConsumerStrategy(new XXHashUnsafe()))
				.StartFromCurrent());

		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");

		CorrelationIdPropertyContext.CorrelationIdProperty = "$correlationId2";

		var metaData1 = Encoding.UTF8.GetBytes(@"{ ""x"": ""x"", ""$correlationId2"": ""1234567890"", ""y"": ""y"" }");
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-1", 0, metaData1));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-1", 1, metaData1));

		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-2", 2, metaData1));
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-2", 3, metaData1));

		Assert.AreEqual(4, client1Envelope.Replies.Count);
		Assert.AreEqual(0, client2Envelope.Replies.Count);

		var metaData2 = Encoding.UTF8.GetBytes(@"{ ""$correlationId2"": ""1234567891"", ""y"": ""y"" }");
		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-1", 4, metaData2));

		Assert.AreEqual(4, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.NotifyLiveSubscriptionMessage(Helper.BuildFakeEventWithMetadata(Guid.NewGuid(), "type", "streamName-3", 5, metaData2));

		Assert.AreEqual(4, client1Envelope.Replies.Count);
		Assert.AreEqual(2, client2Envelope.Replies.Count);
	}
}
