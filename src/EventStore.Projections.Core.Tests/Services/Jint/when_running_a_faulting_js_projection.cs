// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Jint.Runtime;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

public class when_running_a_faulting_js_projection {
	[TestFixture]
	public class when_event_handler_throws : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromAll();
                    on_any(function(state, event) {
                        log(state.count);
                        throw ""failed"";
                        return state;
                    });
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void process_event_throws_javascript_exception() {
			try {
				string state;
				EmittedEventEnvelope[] emittedEvents;
				_stateHandler.ProcessEvent(
					"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0,
					"metadata",
					@"{""a"":""b""}", out state, out emittedEvents);
			} catch (Exception ex) {
				Assert.IsInstanceOf<JavaScriptException>(ex);
				Assert.AreEqual("failed", ex.Message);
			}
		}
	}

	[TestFixture]
	public class when_state_transform_throws : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromAll().when({$any: function(state, event) {
                        return state;
                    }})
                    .transformBy(function (s) { throw ""failed"";});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void process_event_throws_javascript_exception() {
			try {
				string state;
				EmittedEventEnvelope[] emittedEvents;
				Assert.DoesNotThrow(() => _stateHandler.ProcessEvent(
					"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0,
					"metadata",
					@"{""a"":""b""}", out state, out emittedEvents));
				_stateHandler.TransformStateToResult();
			} catch (Exception ex) {
				Assert.IsInstanceOf<JavaScriptException>(ex);
				Assert.AreEqual("failed", ex.Message);
			}
		}
	}
}
