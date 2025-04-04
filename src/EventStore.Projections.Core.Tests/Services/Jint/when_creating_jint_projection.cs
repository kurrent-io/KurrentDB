// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Jint.Runtime;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

class when_creating_jint_projection {
	private ProjectionStateHandlerFactory _stateHandlerFactory;
	private const string _projectionType = "js";

	[SetUp]
	public void Setup() {
		_stateHandlerFactory =
			new ProjectionStateHandlerFactory(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(100));
	}

	[Test, Category(_projectionType)]
	public void it_can_be_created() {
		using (_stateHandlerFactory.Create(_projectionType, @"", true, null)) {
		}
	}

	[Test, Category(_projectionType)]
	public void js_syntax_errors_are_reported() {
		try {
			using (_stateHandlerFactory.Create(_projectionType, @"log(1;", true, null, logger: (s, _) => { })) {
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<JavaScriptException>(ex);
		}
	}

	[Test, Category(_projectionType)]
	public void js_exceptions_errors_are_reported() {
		try {
			using (_stateHandlerFactory.Create(_projectionType, @"throw 123;", true, null, logger: (s, _) => { })) {
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<JavaScriptException>(ex);
			Assert.AreEqual("123", ex.Message);
		}
	}

	[Test, Category(_projectionType)]
	public void long_compilation_times_out() {
		try {
			using (_stateHandlerFactory.Create(_projectionType,
				@"
                                var i = 0;
                                while (true) i++;
                    ",
				true,
				null,
				logger: (s, _) => { })) {
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<TimeoutException>(ex);
		}
	}

	[Test, Category(_projectionType)]
	public void long_execution_times_out() {
		try {
			using (var h = _stateHandlerFactory.Create(_projectionType,
				@"
                        fromAll().when({
                            $any: function (s, e) {
                                log('1');
                                var i = 0;
                                while (true) i++;
                            }
                        });
                    ",
				true,
				null,
				logger: Console.WriteLine)) {
				h.Initialize();
				string newState;
				EmittedEventEnvelope[] emittedevents;
				h.ProcessEvent(
					"partition",
					CheckpointTag.FromPosition(0, 100, 50),
					"stream",
					"event",
					"",
					Guid.NewGuid(),
					1,
					"", "{}",
					out newState, out emittedevents);
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<TimeoutException>(ex);
		}
	}

	[Test, Category(_projectionType)]
	public void long_post_processing_times_out() {
		try {
			using (var h = _stateHandlerFactory.Create(_projectionType,
				@"
                        fromAll().when({
                            $any: function (s, e) {
                                return {};
                            }
                        })
                        .transformBy(function(s){
                                log('1');
                                var i = 0;
                                while (true) i++;
                        });
                    ",
				true,
				null,
				logger: Console.WriteLine)) {
				h.Initialize();
				string newState;
				EmittedEventEnvelope[] emittedevents;
				h.ProcessEvent(
					"partition", CheckpointTag.FromPosition(0, 100, 50), "stream", "event", "", Guid.NewGuid(), 1,
					"", "{}",
					out newState, out emittedevents);
				h.TransformStateToResult();
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<TimeoutException>(ex);
		}
	}

	[Test, Category(_projectionType)]
	public void long_execution_times_out_many() {
		for (var i = 0; i < 10; i++) {
			try {
				using (var h = _stateHandlerFactory.Create(
					_projectionType, @"
                    fromAll().when({
                        $any: function (s, e) {
                            log('1');
                            var i = 0;
                            while (true) i++;
                        }
                    });
                ", true, null, logger: Console.WriteLine)) {
					h.Initialize();
					string newState;
					EmittedEventEnvelope[] emittedevents;
					h.ProcessEvent(
						"partition", CheckpointTag.FromPosition(0, 100, 50), "stream", "event", "", Guid.NewGuid(),
						1,
						"", "{}", out newState, out emittedevents);
					Assert.Fail("Timeout didn't happen");
				}
			} catch (TimeoutException) {
			}
		}
	}
}
