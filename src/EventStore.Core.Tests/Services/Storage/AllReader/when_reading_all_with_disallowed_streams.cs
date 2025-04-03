// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client.Messages;
using NUnit.Framework;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;


namespace EventStore.Core.Tests.Services.Storage.AllReader;

[TestFixture(typeof(LogFormat.V2), typeof(string), "$persistentsubscription-$all::group-checkpoint")]
[TestFixture(typeof(LogFormat.V2), typeof(string), "$persistentsubscription-$all::group-parked")]
[TestFixture(typeof(LogFormat.V3), typeof(uint), "$persistentsubscription-$all::group-checkpoint")]
[TestFixture(typeof(LogFormat.V3), typeof(uint), "$persistentsubscription-$all::group-parked")]
public class when_reading_all_with_disallowed_streams<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	TFPos _forwardReadPos;
	TFPos _backwardReadPos;
	private string _disallowedStream;
	private string _allowedStream1 = "ES1";
	private string _allowedStream2 = "$persistentsubscription-$all::group-somethingallowed";

	public when_reading_all_with_disallowed_streams(string disallowedStream) {
		_disallowedStream = disallowedStream;
	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var firstEvent = await WriteSingleEvent(_allowedStream1, 1, new string('.', 3000), eventId: Guid.NewGuid(),
			eventType: "event-type-1", retryOnFail: true, token: token);
		await WriteSingleEvent(_disallowedStream, 1, new string('.', 3000), eventId: Guid.NewGuid(), eventType: "event-type-2",
			retryOnFail: true, token: token); //disallowed
		await WriteSingleEvent(_allowedStream2, 1, new string('.', 3000), eventId: Guid.NewGuid(), eventType: "event-type-3",
			retryOnFail: true, token: token); //allowed

		_forwardReadPos = new TFPos(firstEvent.LogPosition, firstEvent.LogPosition);
		_backwardReadPos = new TFPos(Writer.Position, Writer.Position);
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_forward() {
		var records = (await ReadIndex.ReadAllEventsForward(_forwardReadPos, 10, CancellationToken.None))
			.EventRecords();
		Assert.AreEqual(2, records.Count);
		Assert.True(records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_forward_with_event_type_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Prefix, new[] {"event-type"});
		var eventFilter = EventFilter.Get(true, filter);

		var result = await ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter, CancellationToken.None);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_forward_with_event_type_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Regex, new[] {@"^.*event-type-.*$"});
		var eventFilter = EventFilter.Get(true, filter);

		var result = await ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter, CancellationToken.None);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_forward_with_stream_id_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Prefix, new[] {"$persistentsubscripti"});
		var eventFilter = EventFilter.Get(true, filter);

		var result = await ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter, CancellationToken.None);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_forward_with_stream_id_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Regex, new[] {@"^.*istentsubsc.*$"});
		var eventFilter = EventFilter.Get(true, filter);

		var result = await ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter, CancellationToken.None);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_backward() {
		var records = (await ReadIndex.ReadAllEventsBackward(_backwardReadPos, 10, CancellationToken.None))
			.EventRecords();
		Assert.AreEqual(2, records.Count);
		Assert.True(records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_backward_with_event_type_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Prefix, ["event-type"]);
		var eventFilter = EventFilter.Get(true, filter);

		var result =
			await ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter,
				CancellationToken.None);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_backward_with_event_type_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Regex, [@"^.*event-type-.*$"]);
		var eventFilter = EventFilter.Get(true, filter);

		var result =
			await ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter,
				CancellationToken.None);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_backward_with_stream_id_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Prefix, ["$persistentsubscripti"]);
		var eventFilter = EventFilter.Get(true, filter);

		var result =
			await ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter,
				CancellationToken.None);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public async Task should_filter_out_disallowed_streams_when_reading_events_backward_with_stream_id_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Regex, [@"^.*istentsubsc.*$"]);
		var eventFilter = EventFilter.Get(true, filter);

		var result =
			await ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter,
				CancellationToken.None);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}
}
