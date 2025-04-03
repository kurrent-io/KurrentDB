// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

public class when_running_with_content_type_validation {
	[TestFixture]
	public class when_running_with_content_type_validation_enabled : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                fromAll().when({$any:
                    function(state, event) {
                    linkTo('output-stream' + event.sequenceNumber, event);
                    return {};
                }});
            ";
		}

		protected override IProjectionStateHandler CreateStateHandler() {
			return _stateHandlerFactory.Create(
				_projectionType, _projection,
				enableContentTypeValidation: true,
				null,
				logger: (s, _) => {
					if (s.StartsWith("P:"))
						Console.WriteLine(s);
					else
						_logged.Add(s);
				}); // skip prelude debug output
		}

		[Test, Category(_projectionType)]
		public void process_null_json_event_does_not_emit() {
			string state = null;
			EmittedEventEnvelope[] emittedEvents = null;

			var result = _stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				null, out state, out emittedEvents, isJson: true);

			Assert.IsNull(emittedEvents);
		}

		[Test, Category(_projectionType)]
		public void process_null_non_json_event_does_emit() {
			string state = null;
			EmittedEventEnvelope[] emittedEvents = null;

			var result = _stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				null, out state, out emittedEvents, isJson: false);

			Assert.IsNotNull(emittedEvents);
			Assert.AreEqual(1, emittedEvents.Length);
		}
	}

	[TestFixture]
	public class when_running_with_content_type_validation_disabled : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                fromAll().when({$any:
                    function(state, event) {
                    linkTo('output-stream' + event.sequenceNumber, event);
                    return {};
                }});
            ";
		}

		protected override IProjectionStateHandler CreateStateHandler() {
			return _stateHandlerFactory.Create(
				_projectionType, _projection,
				enableContentTypeValidation: false,
				projectionExecutionTimeout: null,
				logger: (s, _) => {
					if (s.StartsWith("P:"))
						Console.WriteLine(s);
					else
						_logged.Add(s);
				}); // skip prelude debug output
		}

		[Test, Category(_projectionType)]
		public void process_null_json_event_does_not_emit() {
			string state = null;
			EmittedEventEnvelope[] emittedEvents = null;

			var result = _stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				null, out state, out emittedEvents, isJson: true);

			Assert.IsNull(emittedEvents);
		}

		[Test, Category(_projectionType)]
		public void process_null_non_json_event_does_not_emit() {
			string state = null;
			EmittedEventEnvelope[] emittedEvents = null;

			var result = _stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				null, out state, out emittedEvents, isJson: false);

			Assert.IsNull(emittedEvents);
		}
	}
}
