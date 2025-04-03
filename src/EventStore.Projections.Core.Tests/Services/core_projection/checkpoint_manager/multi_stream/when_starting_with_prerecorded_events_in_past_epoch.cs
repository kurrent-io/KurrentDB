// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_with_prerecorded_events_in_past_epoch<TLogFormat, TStreamId> : TestFixtureWithMultiStreamCheckpointManager<TLogFormat, TStreamId> {
	private readonly CheckpointTag _tag1 =
		CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"a", 0}, {"b", 0}, {"c", 1}});

	private readonly CheckpointTag _tag2 =
		CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"a", 1}, {"b", 0}, {"c", 1}});

	//private readonly CheckpointTag _tag3 =CheckpointTag.FromStreamPositions(new Dictionary<string, int> {{"a", 1}, {"b", 1}, {"c", 1}});

	protected override void Given() {
		base.Given();
		_projectionVersion = new ProjectionVersion(1, 2, 2);
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""v"":2, ""s"": {""a"": 0, ""b"": 0, ""c"": 0}}",
			"{}");
		ExistingEvent("a", "StreamCreated", "", "");
		ExistingEvent("b", "StreamCreated", "", "");
		ExistingEvent("c", "StreamCreated", "", "");

		ExistingEvent("a", "Event", "", @"{""data"":""a""");
		ExistingEvent("b", "Event", "", @"{""data"":""b""");
		ExistingEvent("c", "Event", "", @"{""data"":""c""");

		ExistingEvent("$projections-projection-order", "$>", @"{""v"":1, ""s"": {""a"": 0, ""b"": 0, ""c"": 0}}",
			"0@c");
		ExistingEvent("$projections-projection-order", "$>", @"{""v"":1, ""s"": {""a"": 0, ""b"": 0, ""c"": 1}}",
			"1@c");
		ExistingEvent("$projections-projection-order", "$>", @"{""v"":1, ""s"": {""a"": 1, ""b"": 0, ""c"": 1}}",
			"1@a");
		ExistingEvent("$projections-projection-order", "$>", @"{""v"":1, ""s"": {""a"": 1, ""b"": 1, ""c"": 1}}",
			"1@b");
		ExistingEvent("$projections-projection-order", "$>", @"{""v"":2, ""s"": {""a"": 0, ""b"": 0, ""c"": 0}}",
			"0@c");
		ExistingEvent("$projections-projection-order", "$>", @"{""v"":2, ""s"": {""a"": 0, ""b"": 0, ""c"": 1}}",
			"1@c");
		ExistingEvent("$projections-projection-order", "$>", @"{""v"":2, ""s"": {""a"": 1, ""b"": 0, ""c"": 1}}",
			"1@a");
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
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"a", 0}, {"b", 0}, {"c", 0}}),
			_projection._checkpointLoadedMessages.Single().CheckpointTag);
		Assert.AreEqual("{}", _projection._checkpointLoadedMessages.Single().CheckpointData);
	}

	[Test]
	public void sends_correct_prerecorded_events_loaded_message() {
		Assert.AreEqual(1, _projection._prerecordedEventsLoadedMessages.Count);
		Assert.AreEqual(
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"a", 1}, {"b", 0}, {"c", 1}}),
			_projection._prerecordedEventsLoadedMessages.Single().CheckpointTag);
	}

	[Test]
	public void sends_committed_event_received_messages_in_correct_order() {
		var messages = HandledMessages.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToList();
		Assert.AreEqual(2, messages.Count);

		var message1 = messages[0];
		var message2 = messages[1];

		Assert.AreEqual(_tag1, message1.CheckpointTag);
		Assert.AreEqual(_tag2, message2.CheckpointTag);
	}
}
