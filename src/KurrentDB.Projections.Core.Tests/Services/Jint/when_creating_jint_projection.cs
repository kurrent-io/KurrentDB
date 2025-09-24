// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Jint.Runtime;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

class when_creating_jint_projection {
	private ProjectionStateHandlerFactory _stateHandlerFactory;
	private const string ProjectionType = "js";

	[SetUp]
	public void Setup() {
		_stateHandlerFactory = new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(100), ProjectionTrackers.NoOp);
	}

	[Test, Category(ProjectionType)]
	public void it_can_be_created() {
		using (_stateHandlerFactory.Create("projection", ProjectionType, "", true, null)) {
		}
	}

	[Test, Category(ProjectionType)]
	public void js_syntax_errors_are_reported() {
		try {
			using (_stateHandlerFactory.Create("projection", ProjectionType, "log(1;", true, null, logger: (_, _) => { })) {
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<JavaScriptException>(ex);
		}
	}

	[Test, Category(ProjectionType)]
	public void js_exceptions_errors_are_reported() {
		try {
			using (_stateHandlerFactory.Create("projection", ProjectionType, "throw 123;", true, null, logger: (_, _) => { })) {
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<JavaScriptException>(ex);
			Assert.AreEqual("123", ex.Message);
		}
	}

	[Test, Category(ProjectionType)]
	public void long_compilation_times_out() {
		try {
			using (_stateHandlerFactory.Create("projection", ProjectionType,
				       """
				       var i = 0;
				       while (true) i++;
				       """,
				true,
				null,
				logger: (s, _) => { })) {
			}
		} catch (Exception ex) {
			Assert.IsInstanceOf<TimeoutException>(ex);
		}
	}

	[Test, Category(ProjectionType)]
	public void long_execution_times_out() {
		try {
			using var h = _stateHandlerFactory.Create("projection", ProjectionType,
				"""
				fromAll().when({
				  $any: function (s, e) {
				    log('1');
				    var i = 0;
				    while (true) i++;
				  }
				});
				""",
				true,
				null,
				logger: Console.WriteLine);
			h.Initialize();
			h.ProcessEvent(
				"partition",
				CheckpointTag.FromPosition(0, 100, 50),
				"stream",
				"event",
				"",
				Guid.NewGuid(),
				1,
				"", "{}",
				out _, out _);
		} catch (Exception ex) {
			Assert.IsInstanceOf<TimeoutException>(ex);
		}
	}

	[Test, Category(ProjectionType)]
	public void long_post_processing_times_out() {
		try {
			using var h = _stateHandlerFactory.Create("projection", ProjectionType,
				"""
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
				""",
				true,
				null,
				logger: Console.WriteLine);
			h.Initialize();
			h.ProcessEvent(
				"partition", CheckpointTag.FromPosition(0, 100, 50), "stream", "event", "", Guid.NewGuid(), 1,
				"", "{}",
				out _, out _);
			h.TransformStateToResult();
		} catch (Exception ex) {
			Assert.IsInstanceOf<TimeoutException>(ex);
		}
	}

	[Test, Category(ProjectionType)]
	public void long_execution_times_out_many() {
		for (var i = 0; i < 10; i++) {
			try {
				using var h = _stateHandlerFactory.Create(
					"projection", ProjectionType, @"
                    fromAll().when({
                        $any: function (s, e) {
                            log('1');
                            var i = 0;
                            while (true) i++;
                        }
                    });
                ", true, null, logger: Console.WriteLine);
				h.Initialize();
				h.ProcessEvent(
					"partition", CheckpointTag.FromPosition(0, 100, 50), "stream", "event", "", Guid.NewGuid(),
					1, "", "{}", out _, out _);
				Assert.Fail("Timeout didn't happen");
			} catch (TimeoutException) {
			}
		}
	}
}
