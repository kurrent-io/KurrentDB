// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Linq;
using EventStore.Core.Data;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_single_deleted_event_stream_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _prepare1;
	private EventRecord _delete1;

	protected override void WriteTestScenario() {
		_prepare1 = WriteSingleEvent("ES", 0, "test1");
		_delete1 = WriteDelete("ES");
	}

	[Test]
	public void return_minus_one_for_nonexistent_stream_as_last_event_version() {
		Assert.AreEqual(-1, ReadIndex.GetStreamLastEventNumber("ES-NONEXISTENT"));
	}

	[Test]
	public void return_empty_range_on_from_start_range_query_for_non_existing_stream() {
		var result = ReadIndex.ReadStreamEventsForward("ES-NONEXISTING", 0, 1);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public void return_empty_range_on_from_end_range_query_for_non_existing_stream() {
		var result = ReadIndex.ReadStreamEventsBackward("ES-NONEXISTING", 0, 1);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public void return_not_found_for_get_record_from_non_existing_stream() {
		var result = ReadIndex.ReadEvent("ES-NONEXISTING", 0);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void return_correct_event_version_for_deleted_stream() {
		Assert.AreEqual(EventNumber.DeletedStream, ReadIndex.GetStreamLastEventNumber("ES"));
	}

	[Test]
	public void return_stream_deleted_result_for_deleted_event_stream() {
		var result = ReadIndex.ReadEvent("ES", 0);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void return_empty_range_on_from_start_range_query_for_deleted_event_stream() {
		var result = ReadIndex.ReadStreamEventsForward("ES", 0, 1);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public void return_empty_range_on_from_end_range_query_for_deleted_event_stream() {
		var result = ReadIndex.ReadStreamEventsBackward("ES", 0, 1);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public void return_correct_last_event_version_for_nonexistent_stream_with_same_hash_as_deleted_one() {
		Assert.AreEqual(-1, ReadIndex.GetStreamLastEventNumber("AB"));
	}

	[Test]
	public void not_find_record_for_nonexistent_event_stream_with_same_hash_as_deleted_one() {
		var result = ReadIndex.ReadEvent("AB", 0);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void
		return_empty_range_on_from_start_query_for_nonexisting_event_stream_with_same_hash_as_deleted_one() {
		var result = ReadIndex.ReadStreamEventsForward("HG", 0, 1);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public void return_empty_on_from_end_query_for_nonexisting_event_stream_with_same_hash_as_deleted_one() {
		var result = ReadIndex.ReadStreamEventsBackward("HG", 0, 1);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public void not_find_record_with_nonexistent_version_for_deleted_event_stream() {
		var result = ReadIndex.ReadEvent("ES", 1);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void not_find_record_with_non_existing_version_for_event_stream_with_same_hash_as_deleted_one() {
		var result = ReadIndex.ReadEvent("CL", 1);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void return_all_events_on_read_all_forward() {
		var events = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100).EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_prepare1, events[0]);
		Assert.AreEqual(_delete1, events[1]);
	}

	[Test]
	public void return_all_events_on_read_all_backward() {
		var events = ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100).EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_delete1, events[0]);
		Assert.AreEqual(_prepare1, events[1]);
	}
}
