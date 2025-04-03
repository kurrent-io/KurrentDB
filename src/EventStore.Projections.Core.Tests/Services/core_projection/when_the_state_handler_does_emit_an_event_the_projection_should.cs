// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_the_state_handler_does_emit_an_event_the_projection_should<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private Guid _causingEventId;

	protected override void Given() {
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override void When() {
		//projection subscribes here
		_causingEventId = Guid.NewGuid();
		var committedEventReceived =
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					_causingEventId, "no_state_emit1_type", false, "data",
					"metadata"), _subscriptionId, 0);
		_bus.Publish(committedEventReceived);
	}

	[Test]
	public void write_the_emitted_event() {
		Assert.IsTrue(
			_writeEventHandler.HandledMessages.Any(
				v => Helper.UTF8NoBom.GetString(v.Events[0].Data) == FakeProjectionStateHandler._emit1Data));
	}

	[Test]
	public void set_a_caused_by_position_attributes() {
		var metadata = _writeEventHandler.HandledMessages[0].Events[0].Metadata
			.ParseCheckpointTagVersionExtraJson(default(ProjectionVersion));
		Assert.AreEqual(120, metadata.Tag.CommitPosition);
		Assert.AreEqual(110, metadata.Tag.PreparePosition);
	}
}
