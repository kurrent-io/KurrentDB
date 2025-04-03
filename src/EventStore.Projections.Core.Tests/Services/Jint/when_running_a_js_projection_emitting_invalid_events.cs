// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

public abstract class when_running_a_js_projection_emitting_invalid_events : TestFixtureWithInterpretedProjection {
	
	protected override void Given() {
		_projection = @"
			              fromAll().when({$any: 
			                  function(state, event) {
			                      emit(" + FormatEmitParameters +@");
			                  return {};
			              }});
			          ";
	}

	protected virtual string StreamId => "'testStream'";
	protected virtual string EventType => "'testEventType'";
	protected virtual string EventBody => "{}";
	protected virtual string FormatEmitParameters => $"{StreamId}, {EventType}, {EventBody}";
	protected virtual bool IsNull => false;
	protected abstract string ParameterName { get; }
	[Test, Category(_projectionType)]
	public void process_event_faults() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		TestDelegate td = () =>
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				@"{""a"":""b""}", out state, out emittedEvents);
		var ae = IsNull ? Assert.Throws<ArgumentNullException>(td) : Assert.Throws<ArgumentException>(td);
		Assert.AreEqual(ParameterName, ae.ParamName);
	}

	public class null_stream : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "streamId";
		protected override string StreamId => "null";
		protected override bool IsNull => true;
	}

	public class empty_stream : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "streamId";
		protected override string StreamId => "''";
		protected override bool IsNull => true;
	}

	public class object_stream : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "streamId";
		protected override string StreamId => "{\"a\":\"b\"}";
	}

	public class undefined_stream : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "streamId";
		protected override string StreamId => "undefined";
		protected override bool IsNull => true;

	}


	public class null_eventtype : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventName";
		protected override string EventType => "null";
		protected override bool IsNull => true;
	}

	public class empty_eventtype : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventName";
		protected override string EventType => "''";
		protected override bool IsNull => true;
	}

	public class object_eventtype : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventName";
		protected override string EventType => "{\"a\":\"b\"}";
	}

	public class undefined_eventtype : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventName";
		protected override string EventType => "undefined";
		protected override bool IsNull => true;
	}

	public class null_eventbody : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventBody";
		protected override string EventBody => "null";
		protected override bool IsNull => true;
	}

	public class nonobject_eventbody : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventBody";
		protected override string EventBody => "'test'";
	}

	public class undefined_eventbody : when_running_a_js_projection_emitting_invalid_events {
		protected override string ParameterName => "eventBody";
		protected override bool IsNull => true;
		protected override string EventBody => "undefined";
	}
}
