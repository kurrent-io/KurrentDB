// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Client.Messages;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;


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

	protected override void WriteTestScenario() {
		var firstEvent = WriteSingleEvent(_allowedStream1, 1, new string('.', 3000), eventId: Guid.NewGuid(),
			eventType: "event-type-1", retryOnFail: true);
		WriteSingleEvent(_disallowedStream, 1, new string('.', 3000), eventId: Guid.NewGuid(), eventType: "event-type-2",
			retryOnFail: true); //disallowed
		WriteSingleEvent(_allowedStream2, 1, new string('.', 3000), eventId: Guid.NewGuid(), eventType: "event-type-3",
			retryOnFail: true); //allowed

		_forwardReadPos = new TFPos(firstEvent.LogPosition, firstEvent.LogPosition);
		_backwardReadPos = new TFPos(Writer.Position, Writer.Position);
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_forward() {
		var records = ReadIndex.ReadAllEventsForward(_forwardReadPos, 10).EventRecords();
		Assert.AreEqual(2, records.Count);
		Assert.True(records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_forward_with_event_type_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Prefix, new[] { "event-type" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_forward_with_event_type_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Regex, new[] { @"^.*event-type-.*$" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_forward_with_stream_id_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Prefix, new[] { "$persistentsubscripti" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_forward_with_stream_id_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Regex, new[] { @"^.*istentsubsc.*$" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsForwardFiltered(_forwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_backward() {
		var records = ReadIndex.ReadAllEventsBackward(_backwardReadPos, 10).EventRecords();
		Assert.AreEqual(2, records.Count);
		Assert.True(records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_backward_with_event_type_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Prefix, new[] { "event-type" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_backward_with_event_type_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.EventType,
			Filter.Types.FilterType.Regex, new[] { @"^.*event-type-.*$" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(2, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream1));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_backward_with_stream_id_prefix() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Prefix, new[] { "$persistentsubscripti" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

	[Test]
	public void should_filter_out_disallowed_streams_when_reading_events_backward_with_stream_id_regex() {
		var filter = new Filter(
			Filter.Types.FilterContext.StreamId,
			Filter.Types.FilterType.Regex, new[] { @"^.*istentsubsc.*$" });
		var eventFilter = EventFilter.Get(true, filter);

		var result = ReadIndex.ReadAllEventsBackwardFiltered(_backwardReadPos, 10, 10, eventFilter);
		Assert.AreEqual(1, result.Records.Count);
		Assert.True(result.Records.All(x => x.Event.EventStreamId != _disallowedStream));
		Assert.True(result.Records.Any(x => x.Event.EventStreamId == _allowedStream2));
	}

}
