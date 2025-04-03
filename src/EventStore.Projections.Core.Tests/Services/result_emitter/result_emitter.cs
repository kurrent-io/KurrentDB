// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.result_emitter;

public static class result_emitter {
	[TestFixture]
	public class when_creating {
		private ProjectionNamesBuilder _namesBuilder;

		[SetUp]
		public void setup() {
			_namesBuilder = ProjectionNamesBuilder.CreateForTest("projection");
		}

		[Test]
		public void it_can_be_created() {
			new ResultEventEmitter(_namesBuilder);
		}

		[Test]
		public void null_names_builder_throws_argument_null_exception() {
			Assert.Throws<ArgumentNullException>(() => { new ResultEventEmitter(null); });
		}
	}

	[TestFixture]
	public class when_result_updated {
		private ProjectionNamesBuilder _namesBuilder;
		private ResultEventEmitter _re;
		private string _partition;
		private string _projection;
		private CheckpointTag _resultAt;
		private EmittedEventEnvelope[] _emittedEvents;
		private string _result;

		[SetUp]
		public void setup() {
			Given();
			When();
		}

		private void Given() {
			_projection = "projection";
			_resultAt = CheckpointTag.FromPosition(0, 100, 50);
			_partition = "partition";
			_result = "{\"result\":1}";
			_namesBuilder = ProjectionNamesBuilder.CreateForTest(_projection);
			_re = new ResultEventEmitter(_namesBuilder);
		}

		private void When() {
			_emittedEvents = _re.ResultUpdated(_partition, _result, _resultAt);
		}

		[Test]
		public void emits_result_event() {
			Assert.NotNull(_emittedEvents);
			Assert.AreEqual(2, _emittedEvents.Length);
			var @event = _emittedEvents[0];
			var link = _emittedEvents[1].Event;

			Assert.AreEqual("Result", @event.Event.EventType);
			Assert.AreEqual(_result, @event.Event.Data);
			Assert.AreEqual("$projections-projection-partition-result", @event.Event.StreamId);
			Assert.AreEqual(_resultAt, @event.Event.CausedByTag);
			Assert.IsNull(@event.Event.ExpectedTag);

			Assert.AreEqual("$>", link.EventType);
			((EmittedLinkTo)link).SetTargetEventNumber(1);
			Assert.AreEqual("1@$projections-projection-partition-result", link.Data);
			Assert.AreEqual("$projections-projection-result", link.StreamId);
			Assert.AreEqual(_resultAt, link.CausedByTag);
			Assert.IsNull(link.ExpectedTag);
		}
	}

	[TestFixture]
	public class when_result_removed {
		private ProjectionNamesBuilder _namesBuilder;
		private ResultEventEmitter _re;
		private string _partition;
		private string _projection;
		private CheckpointTag _resultAt;
		private EmittedEventEnvelope[] _emittedEvents;

		[SetUp]
		public void setup() {
			Given();
			When();
		}

		private void Given() {
			_projection = "projection";
			_resultAt = CheckpointTag.FromPosition(0, 100, 50);
			_partition = "partition";
			_namesBuilder = ProjectionNamesBuilder.CreateForTest(_projection);
			_re = new ResultEventEmitter(_namesBuilder);
		}

		private void When() {
			_emittedEvents = _re.ResultUpdated(_partition, null, _resultAt);
		}

		[Test]
		public void emits_result_event() {
			Assert.NotNull(_emittedEvents);
			Assert.AreEqual(2, _emittedEvents.Length);
			var @event = _emittedEvents[0];
			var link = _emittedEvents[1].Event;

			Assert.AreEqual("ResultRemoved", @event.Event.EventType);
			Assert.IsNull(@event.Event.Data);
			Assert.AreEqual("$projections-projection-partition-result", @event.Event.StreamId);
			Assert.AreEqual(_resultAt, @event.Event.CausedByTag);
			Assert.IsNull(@event.Event.ExpectedTag);

			Assert.AreEqual("$>", link.EventType);
			((EmittedLinkTo)link).SetTargetEventNumber(1);
			Assert.AreEqual("1@$projections-projection-partition-result", link.Data);
			Assert.AreEqual("$projections-projection-result", link.StreamId);
			Assert.AreEqual(_resultAt, link.CausedByTag);
			Assert.IsNull(link.ExpectedTag);
		}
	}

	[TestFixture]
	public class when_result_updated_on_root_partition {
		private ProjectionNamesBuilder _namesBuilder;
		private ResultEventEmitter _re;
		private string _partition;
		private string _projection;
		private CheckpointTag _resultAt;
		private EmittedEventEnvelope[] _emittedEvents;
		private string _result;

		[SetUp]
		public void setup() {
			Given();
			When();
		}

		private void Given() {
			_projection = "projection";
			_resultAt = CheckpointTag.FromPosition(0, 100, 50);
			_partition = "";
			_result = "{\"result\":1}";
			_namesBuilder = ProjectionNamesBuilder.CreateForTest(_projection);
			_re = new ResultEventEmitter(_namesBuilder);
		}

		private void When() {
			_emittedEvents = _re.ResultUpdated(_partition, _result, _resultAt);
		}

		[Test]
		public void emits_result_event() {
			Assert.NotNull(_emittedEvents);
			Assert.AreEqual(1, _emittedEvents.Length);
			var @event = _emittedEvents[0].Event;

			Assert.AreEqual("Result", @event.EventType);
			Assert.AreEqual(_result, @event.Data);
			Assert.AreEqual("$projections-projection-result", @event.StreamId);
			Assert.AreEqual(_resultAt, @event.CausedByTag);
			Assert.IsNull(@event.ExpectedTag);
		}
	}
}
