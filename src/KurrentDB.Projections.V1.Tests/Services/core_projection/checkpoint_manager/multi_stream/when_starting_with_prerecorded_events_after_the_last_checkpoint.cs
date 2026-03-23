// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_with_prerecorded_events_after_the_last_checkpoint<TLogFormat, TStreamId> :
	TestFixtureWithMultiStreamCheckpointManager<TLogFormat, TStreamId> {
	private readonly CheckpointTag _tag1 =
		CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> { { "a", 0 }, { "b", 0 }, { "c", 1 } });

	private readonly CheckpointTag _tag2 =
		CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> { { "a", 1 }, { "b", 0 }, { "c", 1 } });

	private readonly CheckpointTag _tag3 =
		CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> { { "a", 1 }, { "b", 1 }, { "c", 1 } });

	protected override void Given() {
		base.Given();
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""s"": {""a"": 0, ""b"": 0, ""c"": 0}}", "{}");
		ExistingEvent("a", "StreamCreated", "", "");
		ExistingEvent("b", "StreamCreated", "", "");
		ExistingEvent("c", "StreamCreated", "", "");
		ExistingEvent("d", "StreamCreated", "", "");

		ExistingEvent("a", "Event", "", @"{""data"":""a""");
		ExistingEvent("b", "Event", "bb", @"{""data"":""b""");
		ExistingEvent("c", "$>", "{$o:\"org\"}", @"1@d");
		ExistingEvent("d", "Event", "dd", @"{""data"":""d""");

		ExistingEvent(
			"$projections-projection-order", "$>", @"{""s"": {""a"": 0, ""b"": 0, ""c"": 0}}", "0@c");
		ExistingEvent(
			"$projections-projection-order", "$>", @"{""s"": {""a"": 0, ""b"": 0, ""c"": 1}}", "1@c");
		ExistingEvent(
			"$projections-projection-order", "$>", @"{""s"": {""a"": 1, ""b"": 0, ""c"": 1}}", "1@a");
		ExistingEvent(
			"$projections-projection-order", "$>", @"{""s"": {""a"": 1, ""b"": 1, ""c"": 1}}", "1@b");
	}

	protected override void When() {
		base.When();
		_checkpointReader.BeginLoadState();
		var checkpointLoaded =
			_consumer.HandledMessages.OfType<CoreProjectionProcessingMessage.CheckpointLoaded>().First();
		_checkpointWriter.StartFrom(checkpointLoaded.CheckpointTag, checkpointLoaded.CheckpointEventNumber);
		_manager.BeginLoadPrerecordedEvents(checkpointLoaded.CheckpointTag);
	}

	[Test]
	public void sends_correct_checkpoint_loaded_message() {
		Assert.AreEqual(1, _projection._checkpointLoadedMessages.Count);
		Assert.AreEqual(
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> { { "a", 0 }, { "b", 0 }, { "c", 0 } }),
			_projection._checkpointLoadedMessages.Single().CheckpointTag);
		Assert.AreEqual("{}", _projection._checkpointLoadedMessages.Single().CheckpointData);
	}

	[Test]
	public void sends_correct_preprecoded_events_loaded_message() {
		Assert.AreEqual(1, _projection._prerecordedEventsLoadedMessages.Count);
		Assert.AreEqual(
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> { { "a", 1 }, { "b", 1 }, { "c", 1 } }),
			_projection._prerecordedEventsLoadedMessages.Single().CheckpointTag);
	}

	[Test]
	public void sends_committed_event_received_messages_in_correct_order() {
		var messages = HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToList();
		Assert.AreEqual(3, messages.Count);

		var message1 = messages[0];
		var message2 = messages[1];
		var message3 = messages[2];

		Assert.AreEqual(_tag1, message1.CheckpointTag);
		Assert.AreEqual(_tag2, message2.CheckpointTag);
		Assert.AreEqual(_tag3, message3.CheckpointTag);
	}

	[Test]
	public void sends_correct_committed_event_received_messages() {
		var messages = HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToList();
		Assert.AreEqual(3, messages.Count);

		var message1 = messages[0];
		var message2 = messages[1];
		var message3 = messages[2];

		Assert.AreEqual(@"{""data"":""d""", message1.Data.Data);
		Assert.AreEqual(@"{""data"":""a""", message2.Data.Data);
		Assert.AreEqual(@"{""data"":""b""", message3.Data.Data);

		Assert.AreEqual(@"dd", message1.Data.Metadata);
		Assert.IsNull(message2.Data.Metadata);
		Assert.AreEqual(@"bb", message3.Data.Metadata);

		Assert.AreEqual("{$o:\"org\"}", message1.Data.PositionMetadata);
		Assert.IsNull(message2.Data.PositionMetadata);
		Assert.IsNull(message3.Data.PositionMetadata);

		Assert.AreEqual("Event", message1.Data.EventType);
		Assert.AreEqual("Event", message2.Data.EventType);
		Assert.AreEqual("Event", message3.Data.EventType);

		Assert.AreEqual("c", message1.Data.PositionStreamId);
		Assert.AreEqual("a", message2.Data.PositionStreamId);
		Assert.AreEqual("b", message3.Data.PositionStreamId);

		Assert.AreEqual("d", message1.Data.EventStreamId);
		Assert.AreEqual("a", message2.Data.EventStreamId);
		Assert.AreEqual("b", message3.Data.EventStreamId);

		Assert.AreEqual(_projectionCorrelationId, message1.SubscriptionId);
		Assert.AreEqual(_projectionCorrelationId, message2.SubscriptionId);
		Assert.AreEqual(_projectionCorrelationId, message3.SubscriptionId);

		Assert.AreEqual(true, message1.Data.ResolvedLinkTo);
		Assert.AreEqual(false, message2.Data.ResolvedLinkTo);
		Assert.AreEqual(false, message3.Data.ResolvedLinkTo);
	}
}
