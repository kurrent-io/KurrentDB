// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using EventStore.Core.Data;
using EventStore.Core.Services;
using NUnit.Framework;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Tests.ClientAPI.ExpectedVersion64Bit;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
[Category("ClientAPI"), Category("LongRunning")]
public class
	read_stream_with_link_to_event_with_event_number_greater_than_int_maxvalue<TLogFormat, TStreamId>
	: MiniNodeWithExistingRecords<TLogFormat, TStreamId> {
	private const string StreamName = "read_stream_with_link_to_event_with_event_number_greater_than_int_maxvalue";
	private const long intMaxValue = (long)int.MaxValue;

	private string _linkedStreamName = "linked-" + StreamName;
	private EventRecord _event1, _event2;

	public override async ValueTask WriteTestScenario(CancellationToken token) {
		_event1 = await WriteSingleEvent(StreamName, intMaxValue + 1, new string('.', 3000), token: token);
		_event2 = await WriteSingleEvent(StreamName, intMaxValue + 2, new string('.', 3000), token: token);

		await WriteSingleEvent(_linkedStreamName, 0, string.Format("{0}@{1}", intMaxValue + 1, StreamName),
			eventType: SystemEventTypes.LinkTo, token: token);
		await WriteSingleEvent(_linkedStreamName, 1, string.Format("{0}@{1}", intMaxValue + 2, StreamName),
			eventType: SystemEventTypes.LinkTo, token: token);
	}

	public override async Task Given() {
		_store = BuildConnection(Node);
		await _store.ConnectAsync();
	}

	[Test]
	public async Task should_be_able_to_read_link_stream_forward_and_resolve_link_tos() {
		var readResult = await _store
			.ReadStreamEventsForwardAsync(_linkedStreamName, 0, 100, true, DefaultData.AdminCredentials);
		Assert.AreEqual(SliceReadStatus.Success, readResult.Status);
		Assert.AreEqual(2, readResult.Events.Length);
		Assert.AreEqual(_event1.EventId, readResult.Events[0].Event.EventId);
		Assert.AreEqual(_event2.EventId, readResult.Events[1].Event.EventId);
		Assert.AreEqual(intMaxValue + 1, readResult.Events[0].Event.EventNumber);
		Assert.AreEqual(intMaxValue + 2, readResult.Events[1].Event.EventNumber);
	}

	[Test]
	public async Task should_be_able_to_read_link_stream_backward_and_resolve_link_tos() {
		var readResult = await _store
			.ReadStreamEventsBackwardAsync(_linkedStreamName, 10, 100, true, DefaultData.AdminCredentials);
		Assert.AreEqual(SliceReadStatus.Success, readResult.Status);
		Assert.AreEqual(2, readResult.Events.Length);
		Assert.AreEqual(_event2.EventId, readResult.Events[0].Event.EventId);
		Assert.AreEqual(_event1.EventId, readResult.Events[1].Event.EventId);
		Assert.AreEqual(intMaxValue + 2, readResult.Events[0].Event.EventNumber);
		Assert.AreEqual(intMaxValue + 1, readResult.Events[1].Event.EventNumber);
	}

	[Test]
	public async Task should_be_able_to_read_all_stream_forward_and_resolve_link_tos() {
		var readResult = await _store.ReadAllEventsForwardAsync(Position.Start, 100, true, DefaultData.AdminCredentials)
;
		var linkedEvents = readResult.Events.Where(x => x.OriginalStreamId == _linkedStreamName).ToList();
		Assert.AreEqual(2, linkedEvents.Count());
		Assert.AreEqual(_event1.EventId, linkedEvents[0].Event.EventId);
		Assert.AreEqual(_event2.EventId, linkedEvents[1].Event.EventId);
		Assert.AreEqual(intMaxValue + 1, linkedEvents[0].Event.EventNumber);
		Assert.AreEqual(intMaxValue + 2, linkedEvents[1].Event.EventNumber);
	}

	[Test]
	public async Task should_be_able_to_read_all_stream_backward_and_resolve_link_tos() {
		var readResult = await _store.ReadAllEventsBackwardAsync(Position.End, 100, true, DefaultData.AdminCredentials)
;
		var linkedEvents = readResult.Events.Where(x => x.OriginalStreamId == _linkedStreamName).ToList();
		Assert.AreEqual(2, linkedEvents.Count());
		Assert.AreEqual(_event2.EventId, linkedEvents[0].Event.EventId);
		Assert.AreEqual(_event1.EventId, linkedEvents[1].Event.EventId);
		Assert.AreEqual(intMaxValue + 2, linkedEvents[0].Event.EventNumber);
		Assert.AreEqual(intMaxValue + 1, linkedEvents[1].Event.EventNumber);
	}
}
