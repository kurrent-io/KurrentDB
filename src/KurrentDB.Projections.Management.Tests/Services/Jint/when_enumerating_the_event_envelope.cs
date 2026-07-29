// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

/// <summary>
/// Pins the envelope's own-key set and order, which is the observable contract of the layouts the
/// envelope is built from. Projections can and do branch on whether a member is present, so the key set
/// has to keep matching what the previous implementation produced: body and data appear together and
/// exactly when the event is JSON, while metadata and linkMetadata always appear, carrying null when
/// their document is absent.
/// <para>
/// One deviation is deliberate and cannot be expressed with a fixed layout. The previous envelope
/// materialized a parsed member as a side effect of the first read of it, so <c>'body' in event</c>
/// answered false before anything had read <c>event.body</c> and true afterwards. The answer is now the
/// same before and after -- it describes the event rather than the reading history.
/// </para>
/// </summary>
public abstract class when_enumerating_the_event_envelope : specification_with_event_handled {
	protected override void Given() {
		_projection = @"
            fromAll().when({$any:
                function(state, event) {
                    state.keys = Object.keys(event);
                    state.hasBody = ('body' in event);
                    state.hasData = ('data' in event);
                    state.hasMetadata = event.hasOwnProperty('metadata');
                    state.hasLinkMetadata = event.hasOwnProperty('linkMetadata');
                    state.metadataIsNull = (event.metadata === null);
                    state.linkMetadataIsNull = (event.linkMetadata === null);
                    return state;
                }
            });
        ";
		_state = @"{}";
		_handledEvent = MakeEvent();
	}

	protected abstract ResolvedEvent MakeEvent();

	protected static ResolvedEvent Event(bool isJson, byte[] data, byte[] metadata, byte[] positionMetadata) =>
		new(
			positionStreamId: "test-stream",
			positionSequenceNumber: 1,
			eventStreamId: "test-stream",
			eventSequenceNumber: 1,
			resolvedLinkTo: false,
			position: new TFPos(100, 50),
			eventOrLinkTargetPosition: new TFPos(100, 50),
			eventId: Guid.NewGuid(),
			eventType: "TestEvent",
			isJson: isJson,
			data: data,
			metadata: metadata,
			positionMetadata: positionMetadata,
			streamMetadata: null,
			timestamp: new DateTime(2023, 4, 5, 12, 34, 56, DateTimeKind.Utc));

	protected override void When() {
		_stateHandler.ProcessEvent(
			"test-partition",
			CheckpointTag.FromPosition(0, _handledEvent.Position.CommitPosition, _handledEvent.Position.PreparePosition),
			"test-category",
			_handledEvent,
			out _newState, out _newSharedState, out _emittedEventEnvelopes);
	}

	protected JsonElement State => JsonDocument.Parse(_newState).RootElement;

	protected string[] Keys =>
		State.GetProperty("keys").EnumerateArray().Select(x => x.GetString()).ToArray();

	protected static readonly string[] EagerKeys = [
		"partition", "created", "bodyRaw", "metadataRaw", "streamId", "eventId", "eventType",
		"linkMetadataRaw", "isJson", "category", "sequenceNumber",
	];

	[TestFixture]
	public class with_a_json_event : when_enumerating_the_event_envelope {
		protected override ResolvedEvent MakeEvent() => Event(
			isJson: true,
			data: Encoding.UTF8.GetBytes("""{"the-key":"the-value"}"""),
			metadata: Encoding.UTF8.GetBytes("""{"the-meta-key":"the-meta-value"}"""),
			positionMetadata: Encoding.UTF8.GetBytes("""{"the-link-key":"the-link-value"}"""));

		[Test, Category(_projectionType)]
		public void own_keys_include_the_parsed_body_pair() =>
			Assert.AreEqual(EagerKeys.Concat(["body", "data", "metadata", "linkMetadata"]).ToArray(), Keys);

		[Test, Category(_projectionType)]
		public void parsed_members_answer_existence_questions_without_being_read() {
			// `in` and hasOwnProperty do not observe a lazy slot's value, so these are true whether or not
			// the document behind them has been parsed.
			Assert.IsTrue(State.GetProperty("hasBody").GetBoolean());
			Assert.IsTrue(State.GetProperty("hasData").GetBoolean());
			Assert.IsTrue(State.GetProperty("hasMetadata").GetBoolean());
			Assert.IsTrue(State.GetProperty("hasLinkMetadata").GetBoolean());
		}
	}

	[TestFixture]
	public class with_a_non_json_event : when_enumerating_the_event_envelope {
		protected override ResolvedEvent MakeEvent() => Event(
			isJson: false,
			data: Encoding.UTF8.GetBytes("this is not json"),
			metadata: Encoding.UTF8.GetBytes("""{"the-meta-key":"the-meta-value"}"""),
			positionMetadata: Encoding.UTF8.GetBytes("""{"the-link-key":"the-link-value"}"""));

		[Test, Category(_projectionType)]
		public void body_and_data_are_absent() {
			// The previous envelope only created these when the event was JSON, and a projection may be
			// branching on exactly that.
			Assert.AreEqual(EagerKeys.Concat(["metadata", "linkMetadata"]).ToArray(), Keys);
			Assert.IsFalse(State.GetProperty("hasBody").GetBoolean());
			Assert.IsFalse(State.GetProperty("hasData").GetBoolean());
		}

		[Test, Category(_projectionType)]
		public void metadata_members_are_still_present() {
			Assert.IsTrue(State.GetProperty("hasMetadata").GetBoolean());
			Assert.IsTrue(State.GetProperty("hasLinkMetadata").GetBoolean());
		}
	}

	[TestFixture]
	public class with_no_metadata_documents : when_enumerating_the_event_envelope {
		protected override ResolvedEvent MakeEvent() => Event(
			isJson: true,
			data: Encoding.UTF8.GetBytes("""{"the-key":"the-value"}"""),
			metadata: null,
			positionMetadata: null);

		[Test, Category(_projectionType)]
		public void metadata_members_are_present_and_null() {
			// An absent document was never expressed by omitting the property: the raw slots were always
			// assigned, so the parsed members existed and read as null.
			Assert.AreEqual(EagerKeys.Concat(["body", "data", "metadata", "linkMetadata"]).ToArray(), Keys);
			Assert.IsTrue(State.GetProperty("hasMetadata").GetBoolean());
			Assert.IsTrue(State.GetProperty("hasLinkMetadata").GetBoolean());
			Assert.IsTrue(State.GetProperty("metadataIsNull").GetBoolean());
			Assert.IsTrue(State.GetProperty("linkMetadataIsNull").GetBoolean());
		}
	}
}
