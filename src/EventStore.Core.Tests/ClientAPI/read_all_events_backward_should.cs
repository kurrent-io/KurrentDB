// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Services;
using EventStore.Core.Tests.ClientAPI.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class read_all_events_backward_should<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private EventData[] _testEvents;

	protected override async Task When() {
		await _conn.SetStreamMetadataAsync("$all", -1,
				StreamMetadata.Build().SetReadRole(SystemRoles.All),
				DefaultData.AdminCredentials);

		_testEvents = Enumerable.Range(0, 20).Select(x => TestEvent.NewTestEvent(x.ToString())).ToArray();
		await _conn.AppendToStreamAsync("stream", ExpectedVersion.NoStream, _testEvents);
	}

	[Test, Category("LongRunning")]
	public async Task return_empty_slice_if_asked_to_read_from_start() {
		var read = await _conn.ReadAllEventsBackwardAsync(Position.Start, 1, false);
		Assert.That(read.IsEndOfStream, Is.True);
		Assert.That(read.Events.Length, Is.EqualTo(0));
	}

	[Test, Category("LongRunning")]
	public async Task return_partial_slice_if_not_enough_events() {
		var read = await _conn.ReadAllEventsBackwardAsync(Position.End, 40, false);
		Assert.That(read.Events.Length, Is.LessThan(40));
		Assert.That(EventDataComparer.Equal(_testEvents.Reverse().ToArray(),
			read.Events.Take(_testEvents.Length).Select(x => x.Event).ToArray()));
	}

	[Test, Category("LongRunning")]
	public async Task return_events_in_reversed_order_compared_to_written() {
		var read = await _conn.ReadAllEventsBackwardAsync(Position.End, _testEvents.Length, false);
		Assert.That(EventDataComparer.Equal(_testEvents.Reverse().ToArray(),
			read.Events.Select(x => x.Event).ToArray()));
	}

	[Test, Category("LongRunning")]
	public async Task be_able_to_read_all_one_by_one_until_end_of_stream() {
		var all = new List<RecordedEvent>();
		var position = Position.End;
		AllEventsSlice slice;

		while (!(slice = await _conn.ReadAllEventsBackwardAsync(position, 1, false)).IsEndOfStream) {
			all.Add(slice.Events.Single().Event);
			position = slice.NextPosition;
		}

		Assert.That(
			EventDataComparer.Equal(_testEvents.Reverse().ToArray(), all.Take(_testEvents.Length).ToArray()));
	}

	[Test, Category("LongRunning")]
	public async Task be_able_to_read_events_slice_at_time() {
		var all = new List<RecordedEvent>();
		var position = Position.End;
		AllEventsSlice slice;

		while (!(slice = await _conn.ReadAllEventsBackwardAsync(position, 5, false)).IsEndOfStream) {
			all.AddRange(slice.Events.Select(x => x.Event));
			position = slice.NextPosition;
		}

		Assert.That(
			EventDataComparer.Equal(_testEvents.Reverse().ToArray(), all.Take(_testEvents.Length).ToArray()));
	}

	[Test]
	[Category("Network")]
	public async Task throw_when_got_int_max_value_as_maxcount() {
		await AssertEx.ThrowsAsync<ArgumentException>(
			() => _conn.ReadAllEventsBackwardAsync(Position.Start, int.MaxValue, resolveLinkTos: false));
	}
}
