// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Standard;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.handlers;

public static class categorize_events_by_correlation_id {
	[TestFixture]
	public class when_handling_simple_event {
		private ByCorrelationId _handler;
		private string _state;
		private EmittedEventEnvelope[] _emittedEvents;
		private bool _result;
		private DateTime _dateTime;

		[SetUp]
		public void when() {
			_handler = new ByCorrelationId("", Console.WriteLine);
			_handler.Initialize();
			_dateTime = DateTime.UtcNow;
			var dataBytes = Encoding.ASCII.GetBytes("{}");
			var metadataBytes = Encoding.ASCII.GetBytes("{\"$correlationId\":\"testing1\"}");

			string sharedState;
			_result = _handler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 200, 150), null,
				new ResolvedEvent(
					"cat1-stream1", 10, "cat1-stream1", 10, false, new TFPos(200, 150), new TFPos(200, 150),
					Guid.NewGuid(),
					"event_type", true, dataBytes, metadataBytes, null, null, _dateTime), out _state,
				out sharedState, out _emittedEvents);
		}

		[Test]
		public void result_is_true() {
			Assert.IsTrue(_result);
		}

		[Test]
		public void state_stays_null() {
			Assert.IsNull(_state);
		}

		[Test]
		public void emits_correct_link() {
			Assert.NotNull(_emittedEvents);
			Assert.AreEqual(1, _emittedEvents.Length);
			var @event = _emittedEvents[0].Event;
			Assert.AreEqual("$>", @event.EventType);
			Assert.AreEqual("$bc-testing1", @event.StreamId);
			Assert.AreEqual("10@cat1-stream1", @event.Data);

			string eventTimestampJson = null;
			var extraMetadata = @event.ExtraMetaData();
			foreach (var kvp in extraMetadata) {
				if (kvp.Key.Equals("$eventTimestamp")) {
					eventTimestampJson = kvp.Value;
				}
			}

			Assert.NotNull(eventTimestampJson);
			Assert.AreEqual("\"" + _dateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") + "\"", eventTimestampJson);
		}
	}

	[TestFixture]
	public class when_handling_link_to_event {
		private ByCorrelationId _handler;
		private string _state;
		private EmittedEventEnvelope[] _emittedEvents;
		private bool _result;
		private DateTime _dateTime;
		private Guid _eventId;

		[SetUp]
		public void when() {
			_handler = new ByCorrelationId("", Console.WriteLine);
			_handler.Initialize();
			_dateTime = DateTime.UtcNow;
			string sharedState;

			var dataBytes = Encoding.ASCII.GetBytes("10@cat1-stream1");
			var metadataBytes =
				Encoding.ASCII.GetBytes("{\"$correlationId\":\"testing2\", \"$whatever\":\"hello\"}");
			var myEvent = new ResolvedEvent("cat2-stream2", 20, "cat2-stream2", 20, true, new TFPos(200, 150),
				new TFPos(200, 150), Guid.NewGuid(), "$>", true, dataBytes, metadataBytes, null, null, _dateTime);

			_eventId = myEvent.EventId;
			_result = _handler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 200, 150), null,
				myEvent, out _state, out sharedState, out _emittedEvents);
		}

		[Test]
		public void result_is_true() {
			Assert.IsTrue(_result);
		}

		[Test]
		public void state_stays_null() {
			Assert.IsNull(_state);
		}

		[Test]
		public void emits_correct_link() {
			Assert.NotNull(_emittedEvents);
			Assert.AreEqual(1, _emittedEvents.Length);
			var @event = _emittedEvents[0].Event;
			Assert.AreEqual("$>", @event.EventType);
			Assert.AreEqual("$bc-testing2", @event.StreamId);
			Assert.AreEqual("10@cat1-stream1", @event.Data);

			string eventTimestampJson = null;
			string linkJson = null;
			var extraMetadata = @event.ExtraMetaData();
			foreach (var kvp in extraMetadata) {
				switch (kvp.Key) {
					case "$eventTimestamp":
						eventTimestampJson = kvp.Value;
						break;
					case "$link":
						linkJson = kvp.Value;
						break;
				}
			}

			Assert.NotNull(eventTimestampJson);
			Assert.AreEqual("\"" + _dateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") + "\"", eventTimestampJson);

			//the link's metadata should be copied to $link.metadata and id to $link.eventId
			Assert.NotNull(linkJson);
			var link = JObject.Parse(linkJson);
			Assert.AreEqual(link.GetValue("eventId").ToObject<string>(), _eventId.ToString());

			var linkMetadata = (JObject)link.GetValue("metadata");
			Assert.AreEqual(linkMetadata.GetValue("$correlationId").ToObject<string>(), "testing2");
			Assert.AreEqual(linkMetadata.GetValue("$whatever").ToObject<string>(), "hello");
		}
	}

	[TestFixture]
	public class when_handling_non_json_event {
		private ByCorrelationId _handler;
		private string _state;
		private EmittedEventEnvelope[] _emittedEvents;
		private bool _result;

		[SetUp]
		public void when() {
			_handler = new ByCorrelationId("", Console.WriteLine);
			_handler.Initialize();
			string sharedState;
			_result = _handler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 200, 150), null,
				new ResolvedEvent(
					"cat1-stream1", 10, "cat1-stream1", 10, false, new TFPos(200, 150), Guid.NewGuid(),
					"event_type", false, "non_json_data", "non_json_metadata"), out _state, out sharedState,
				out _emittedEvents);
		}

		[Test]
		public void result_is_false() {
			Assert.IsFalse(_result);
		}

		[Test]
		public void state_stays_null() {
			Assert.IsNull(_state);
		}

		[Test]
		public void does_not_emit_link() {
			Assert.IsNull(_emittedEvents);
		}
	}

	[TestFixture]
	public class when_handling_json_event_with_no_correlation_id {
		private ByCorrelationId _handler;
		private string _state;
		private EmittedEventEnvelope[] _emittedEvents;
		private bool _result;

		[SetUp]
		public void when() {
			_handler = new ByCorrelationId("", Console.WriteLine);
			_handler.Initialize();
			string sharedState;
			_result = _handler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 200, 150), null,
				new ResolvedEvent(
					"cat1-stream1", 10, "cat1-stream1", 10, false, new TFPos(200, 150), Guid.NewGuid(),
					"event_type", true, "{}", "{}"), out _state, out sharedState, out _emittedEvents);
		}

		[Test]
		public void result_is_false() {
			Assert.IsFalse(_result);
		}

		[Test]
		public void state_stays_null() {
			Assert.IsNull(_state);
		}

		[Test]
		public void does_not_emit_link() {
			Assert.IsNull(_emittedEvents);
		}
	}

	[TestFixture]
	public class when_handling_json_event_with_non_json_metadata {
		private ByCorrelationId _handler;
		private string _state;
		private EmittedEventEnvelope[] _emittedEvents;
		private bool _result;

		[SetUp]
		public void when() {
			_handler = new ByCorrelationId("", Console.WriteLine);
			_handler.Initialize();
			string sharedState;
			_result = _handler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 200, 150), null,
				new ResolvedEvent(
					"cat1-stream1", 10, "cat1-stream1", 10, false, new TFPos(200, 150), Guid.NewGuid(),
					"event_type", true, "{}", "non_json_metadata"), out _state, out sharedState,
				out _emittedEvents);
		}

		[Test]
		public void result_is_false() {
			Assert.IsFalse(_result);
		}

		[Test]
		public void state_stays_null() {
			Assert.IsNull(_state);
		}

		[Test]
		public void does_not_emit_link() {
			Assert.IsNull(_emittedEvents);
		}
	}

	[TestFixture]
	public class with_custom_valid_correlation_id_property {
		private ByCorrelationId _handler;
		private string _state;
		private EmittedEventEnvelope[] _emittedEvents;
		private bool _result;
		private string source = "{\"correlationIdProperty\":\"$myCorrelationId\"}";

		[SetUp]
		public void when() {
			_handler = new ByCorrelationId(source, Console.WriteLine);
			_handler.Initialize();
			string sharedState;
			_result = _handler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 200, 150), null,
				new ResolvedEvent(
					"cat1-stream1", 10, "cat1-stream1", 10, false, new TFPos(200, 150), Guid.NewGuid(),
					"event_type", true, "{}", "{\"$myCorrelationId\":\"testing1\"}"), out _state, out sharedState,
				out _emittedEvents);
		}

		[Test]
		public void result_is_true() {
			Assert.IsTrue(_result);
		}

		[Test]
		public void state_stays_null() {
			Assert.IsNull(_state);
		}

		[Test]
		public void emits_correct_link() {
			Assert.NotNull(_emittedEvents);
			Assert.AreEqual(1, _emittedEvents.Length);
			var @event = _emittedEvents[0].Event;
			Assert.AreEqual("$>", @event.EventType);
			Assert.AreEqual("$bc-testing1", @event.StreamId);
			Assert.AreEqual("10@cat1-stream1", @event.Data);
		}
	}

	[TestFixture]
	public class with_custom_invalid_correlation_id_property {
		private string source = "{\"thisisnotvalid\":\"$myCorrelationId\"}";

		[SetUp]
		public void when() {
		}

		[Test]
		public void should_throw_invalid_operation_exception() {
			Assert.Throws<InvalidOperationException>(() => { new ByCorrelationId(source, Console.WriteLine); });
		}
	}
}
