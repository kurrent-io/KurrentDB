// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

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
				"projection", ProjectionType, _projection,
				enableContentTypeValidation: true,
				null,
				logger: (s, _) => {
					if (s.StartsWith("P:"))
						Console.WriteLine(s);
					else
						_logged.Add(s);
				}); // skip prelude debug output
		}

		[Test, Category(ProjectionType)]
		public void process_null_json_event_does_not_emit() {
			var result = _stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata", null, out _, out var emittedEvents, isJson: true);

			Assert.IsNull(emittedEvents);
		}

		[Test, Category(ProjectionType)]
		public void process_null_non_json_event_does_emit() {
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				null, out _, out var emittedEvents, isJson: false);

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
				"projection", ProjectionType, _projection,
				enableContentTypeValidation: false,
				projectionExecutionTimeout: null,
				logger: (s, _) => {
					if (s.StartsWith("P:"))
						Console.WriteLine(s);
					else
						_logged.Add(s);
				}); // skip prelude debug output
		}

		[Test, Category(ProjectionType)]
		public void process_null_json_event_does_not_emit() {
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata", null, out _, out var emittedEvents, isJson: true);

			Assert.IsNull(emittedEvents);
		}

		[Test, Category(ProjectionType)]
		public void process_null_non_json_event_does_not_emit() {
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata", null, out _, out var emittedEvents, isJson: false);

			Assert.IsNull(emittedEvents);
		}
	}
}
