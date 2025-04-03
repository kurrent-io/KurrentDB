// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_having_truncatebefore_set_for_stream<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private EventData[] _testEvents;

	protected override Task When() {
		_testEvents = Enumerable.Range(0, 5).Select(x => TestEvent.NewTestEvent(data: x.ToString())).ToArray();
		return Task.CompletedTask;
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task read_event_respects_truncatebefore() {
		const string stream = "read_event_respects_truncatebefore";
		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[2].EventId, res.Event.Value.OriginalEvent.EventId);
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task read_stream_forward_respects_truncatebefore() {
		const string stream = "read_stream_forward_respects_truncatebefore";
		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task read_stream_backward_respects_truncatebefore() {
		const string stream = "read_stream_backward_respects_truncatebefore";
		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task after_setting_less_strict_truncatebefore_read_event_reads_more_events() {
		const string stream = "after_setting_less_strict_truncatebefore_read_event_reads_more_events";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[2].EventId, res.Event.Value.OriginalEvent.EventId);

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(1));

		res = await _conn.ReadEventAsync(stream, 0, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[1].EventId, res.Event.Value.OriginalEvent.EventId);
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task after_setting_more_strict_truncatebefore_read_event_reads_less_events() {
		const string stream = "after_setting_more_strict_truncatebefore_read_event_reads_less_events";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[2].EventId, res.Event.Value.OriginalEvent.EventId);

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(3));

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 3, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[3].EventId, res.Event.Value.OriginalEvent.EventId);
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task less_strict_max_count_doesnt_change_anything_for_event_read() {
		const string stream = "less_strict_max_count_doesnt_change_anything_for_event_read";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[2].EventId, res.Event.Value.OriginalEvent.EventId);

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(2).SetMaxCount(4));

		res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[2].EventId, res.Event.Value.OriginalEvent.EventId);
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task more_strict_max_count_gives_less_events_for_event_read() {
		const string stream = "more_strict_max_count_gives_less_events_for_event_read";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadEventAsync(stream, 1, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[2].EventId, res.Event.Value.OriginalEvent.EventId);

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(2).SetMaxCount(2));

		res = await _conn.ReadEventAsync(stream, 2, false);
		Assert.AreEqual(EventReadStatus.NotFound, res.Status);

		res = await _conn.ReadEventAsync(stream, 3, false);
		Assert.AreEqual(EventReadStatus.Success, res.Status);
		Assert.AreEqual(_testEvents[3].EventId, res.Event.Value.OriginalEvent.EventId);
	}


	[Test, Category("LongRunning"), Category("Network")]
	public async Task after_setting_less_strict_truncatebefore_read_stream_forward_reads_more_events() {
		const string stream = "after_setting_less_strict_truncatebefore_read_stream_forward_reads_more_events";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(1));

		res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(4, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(1).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task after_setting_more_strict_truncatebefore_read_stream_forward_reads_less_events() {
		const string stream = "after_setting_more_strict_truncatebefore_read_stream_forward_reads_less_events";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(3));

		res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(2, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(3).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task less_strict_max_count_doesnt_change_anything_for_stream_forward_read() {
		const string stream = "less_strict_max_count_doesnt_change_anything_for_stream_forward_read";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(2).SetMaxCount(4));

		res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task more_strict_max_count_gives_less_events_for_stream_forward_read() {
		const string stream = "more_strict_max_count_gives_less_events_for_stream_forward_read";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(2).SetMaxCount(2));

		res = await _conn.ReadStreamEventsForwardAsync(stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(2, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(3).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task after_setting_less_strict_truncatebefore_read_stream_backward_reads_more_events() {
		const string stream = "after_setting_less_strict_truncatebefore_read_stream_backward_reads_more_events";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(1));

		res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(4, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(1).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task after_setting_more_strict_truncatebefore_read_stream_backward_reads_less_events() {
		const string stream = "after_setting_more_strict_truncatebefore_read_stream_backward_reads_less_events";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(3));

		res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(2, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(3).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task less_strict_max_count_doesnt_change_anything_for_stream_backward_read() {
		const string stream = "less_strict_max_count_doesnt_change_anything_for_stream_backward_read";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(2).SetMaxCount(4));

		res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task more_strict_max_count_gives_less_events_for_stream_backward_read() {
		const string stream = "more_strict_max_count_gives_less_events_for_stream_backward_read";

		await _conn.AppendToStreamAsync(stream, ExpectedVersion.NoStream, _testEvents);

		await _conn.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetTruncateBefore(2));

		var res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());

		await _conn.SetStreamMetadataAsync(stream, 0, StreamMetadata.Build().SetTruncateBefore(2).SetMaxCount(2));

		res = await _conn.ReadStreamEventsBackwardAsync(stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(2, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(3).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}
}
