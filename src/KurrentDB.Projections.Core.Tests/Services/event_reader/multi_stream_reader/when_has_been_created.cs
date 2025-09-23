// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_has_been_created<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private Guid _distributionPointCorrelationId;
	private string[] _abStreams;
	private Dictionary<string, long> _ab12Tag;

	[SetUp]
	public new void When() {
		_ab12Tag = new() { { "a", 1 }, { "b", 2 } };
		_abStreams = ["a", "b"];
		_distributionPointCorrelationId = Guid.NewGuid();
		_edp = new(_bus, _distributionPointCorrelationId, null, 0, _abStreams, _ab12Tag, false, new RealTimeProvider());
	}

	[Test]
	public void it_can_be_resumed() {
		_edp.Resume();
	}

	[Test]
	public void it_cannot_be_paused() {
		Assert.Throws<InvalidOperationException>(() => _edp.Pause());
	}

	[Test]
	public void handle_read_events_completed_throws() {
		Assert.Throws<InvalidOperationException>(() => {
			_edp.Handle(
				new ClientMessage.ReadStreamEventsForwardCompleted(
					_distributionPointCorrelationId, "a", 100, 100, ReadStreamResult.Success, [],
					null, false, "", -1, 4, true, 100));
		});
	}
}
