// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_the_state_handler_does_emit_multiple_interleaved_events_into_the_same_stream_the_projection_should<TLogFormat, TStreamId> :
		TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result", @"{""c"": 100, ""p"": 50}", "{}");
		ExistingEvent(
			"$projections-projection-result", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", "{}");
		NoStream(FakeProjectionStateHandler._emit1StreamId);
		NoStream(FakeProjectionStateHandler._emit2StreamId);
	}

	protected override void When() {
		//projection subscribes here
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					Guid.NewGuid(), "emit212_type", false, "data",
					"metadata"), _subscriptionId, 0));
	}
}
