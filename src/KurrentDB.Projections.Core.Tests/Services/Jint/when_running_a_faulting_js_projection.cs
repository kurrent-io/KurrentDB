// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Jint.Runtime;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

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

		[Test, Category(ProjectionType)]
		public void process_event_throws_javascript_exception() {
			try {
				_stateHandler.ProcessEvent(
					"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0,
					"metadata", """{"a":"b"}""", out _, out _);
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

		[Test, Category(ProjectionType)]
		public void process_event_throws_javascript_exception() {
			try {
				Assert.DoesNotThrow(() => _stateHandler.ProcessEvent(
					"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0,
					"metadata", """{"a":"b"}""", out _, out _));
				_stateHandler.TransformStateToResult();
			} catch (Exception ex) {
				Assert.IsInstanceOf<JavaScriptException>(ex);
				Assert.AreEqual("failed", ex.Message);
			}
		}
	}
}
