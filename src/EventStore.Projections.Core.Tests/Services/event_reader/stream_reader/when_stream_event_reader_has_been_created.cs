// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_stream_event_reader_has_been_created<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _edp;

	//private Guid _publishWithCorrelationId;
	private Guid _distibutionPointCorrelationId;

	[SetUp]
	public new void When() {
		//_publishWithCorrelationId = Guid.NewGuid();
		_distibutionPointCorrelationId = Guid.NewGuid();
		_edp = new StreamEventReader(_bus, _distibutionPointCorrelationId, null, "stream", 0,
			new RealTimeProvider(), false,
			produceStreamDeletes: false);
	}

	[Test]
	public void it_can_be_resumed() {
		_edp.Resume();
	}

	[Test]
	public void it_cannot_be_paused() {
		Assert.Throws<InvalidOperationException>(() => { _edp.Pause(); });
	}

	[Test]
	public void handle_read_events_completed_throws() {
		Assert.Throws<InvalidOperationException>(() => {
			_edp.Handle(
				new ClientMessage.ReadStreamEventsForwardCompleted(
					_distibutionPointCorrelationId, "stream", 100, 100, ReadStreamResult.Success,
					new ResolvedEvent[0], null, false, "", -1, 4, true, 100));
		});
	}
}
