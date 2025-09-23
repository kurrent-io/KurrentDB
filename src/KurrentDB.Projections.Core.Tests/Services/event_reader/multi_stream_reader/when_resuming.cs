// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_resuming<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
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
		_edp.Resume();
	}

	[Test]
	public void it_cannot_be_resumed() {
		Assert.Throws<InvalidOperationException>(() => _edp.Resume());
	}

	[Test]
	public void it_cannot_be_paused() {
		_edp.Pause();
	}

	[Test]
	public void it_publishes_read_events_from_beginning() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Count());
		Assert.IsTrue(_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Any(m => m.EventStreamId == "a"));
		Assert.IsTrue(_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Any(m => m.EventStreamId == "b"));
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Single(m => m.EventStreamId == "a").FromEventNumber);
		Assert.AreEqual(
			2,
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Single(m => m.EventStreamId == "b").FromEventNumber);
	}

	[Test]
	public void can_handle_read_events_completed() {
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				_distributionPointCorrelationId, "a", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "a", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type", [], []), 0)
				], null, false, "", 2, 4, false, 100));
	}
}
