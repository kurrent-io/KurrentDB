// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Text;
using System.Text.Json;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_accessing_event_envelope_properties : specification_with_event_handled {
	private static readonly Guid ExpectedEventId = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
	private static readonly DateTime ExpectedTimestamp = new(2023, 4, 5, 12, 34, 56, DateTimeKind.Utc);

	protected override void Given() {
		_projection = @"
            fromAll().when({$any:
                function(state, event) {
                    state.isJson = event.isJson;
                    state.body = event.body;
                    state.data = event.data;
                    state.bodyRaw = event.bodyRaw;
                    state.metadata = event.metadata;
                    state.metadataRaw = event.metadataRaw;
                    state.linkMetadata = event.linkMetadata;
                    state.linkMetadataRaw = event.linkMetadataRaw;
                    state.sequenceNumber = event.sequenceNumber;
                    state.streamId = event.streamId;
                    state.eventType = event.eventType;
                    state.eventId = event.eventId;
                    state.partition = event.partition;
                    state.category = event.category;
                    state.created = event.created;
                    return state;
                }
            });
        ";
		_state = @"{}";
		_handledEvent = new ResolvedEvent(
			positionStreamId: "test-stream",
			positionSequenceNumber: 42,
			eventStreamId: "test-stream",
			eventSequenceNumber: 42,
			resolvedLinkTo: false,
			position: new TFPos(100, 50),
			eventOrLinkTargetPosition: new TFPos(100, 50),
			eventId: ExpectedEventId,
			eventType: "TestEvent",
			isJson: true,
			data: Encoding.UTF8.GetBytes("""{"the-key":"the-value"}"""),
			metadata: Encoding.UTF8.GetBytes("""{"the-meta-key":"the-meta-value"}"""),
			positionMetadata: Encoding.UTF8.GetBytes("""{"the-link-key":"the-link-value"}"""),
			streamMetadata: null,
			timestamp: ExpectedTimestamp);
	}

	protected override void When() {
		_stateHandler.ProcessEvent(
			"test-partition",
			CheckpointTag.FromPosition(0, _handledEvent.Position.CommitPosition, _handledEvent.Position.PreparePosition),
			"test-category",
			_handledEvent,
			out _newState, out _newSharedState, out _emittedEventEnvelopes);
	}

	private JsonElement State => JsonDocument.Parse(_newState).RootElement;

	[Test, Category(_projectionType)]
	public void stream_id_is_accessible() =>
		Assert.AreEqual("test-stream", State.GetProperty("streamId").GetString());

	[Test, Category(_projectionType)]
	public void sequence_number_is_accessible() =>
		Assert.AreEqual(42, State.GetProperty("sequenceNumber").GetInt64());

	[Test, Category(_projectionType)]
	public void event_type_is_accessible() =>
		Assert.AreEqual("TestEvent", State.GetProperty("eventType").GetString());

	[Test, Category(_projectionType)]
	public void event_id_is_accessible() =>
		Assert.AreEqual(ExpectedEventId.ToString(), State.GetProperty("eventId").GetString());

	[Test, Category(_projectionType)]
	public void is_json_is_accessible() =>
		Assert.IsTrue(State.GetProperty("isJson").GetBoolean());

	[Test, Category(_projectionType)]
	public void body_is_accessible() =>
		Assert.AreEqual("the-value", State.GetProperty("body").GetProperty("the-key").GetString());

	[Test, Category(_projectionType)]
	public void data_is_accessible_as_alias_for_body() =>
		Assert.AreEqual("the-value", State.GetProperty("data").GetProperty("the-key").GetString());

	[Test, Category(_projectionType)]
	public void body_raw_is_accessible() =>
		Assert.AreEqual("""{"the-key":"the-value"}""", State.GetProperty("bodyRaw").GetString());

	[Test, Category(_projectionType)]
	public void metadata_is_accessible() =>
		Assert.AreEqual("the-meta-value", State.GetProperty("metadata").GetProperty("the-meta-key").GetString());

	[Test, Category(_projectionType)]
	public void metadata_raw_is_accessible() =>
		Assert.AreEqual("""{"the-meta-key":"the-meta-value"}""", State.GetProperty("metadataRaw").GetString());

	[Test, Category(_projectionType)]
	public void link_metadata_is_accessible() =>
		Assert.AreEqual("the-link-value", State.GetProperty("linkMetadata").GetProperty("the-link-key").GetString());

	[Test, Category(_projectionType)]
	public void link_metadata_raw_is_accessible() =>
		Assert.AreEqual("""{"the-link-key":"the-link-value"}""", State.GetProperty("linkMetadataRaw").GetString());

	[Test, Category(_projectionType)]
	public void partition_is_accessible() =>
		Assert.AreEqual("test-partition", State.GetProperty("partition").GetString());

	[Test, Category(_projectionType)]
	public void category_is_accessible() =>
		Assert.AreEqual("test-category", State.GetProperty("category").GetString());

	[Test, Category(_projectionType)]
	public void created_is_accessible() =>
		Assert.AreEqual(ExpectedTimestamp.ToString("o"), State.GetProperty("created").GetString());
}
