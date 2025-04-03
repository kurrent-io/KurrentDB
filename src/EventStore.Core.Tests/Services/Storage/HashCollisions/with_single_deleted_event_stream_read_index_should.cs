// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_single_deleted_event_stream_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _prepare1;
	private EventRecord _delete1;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare1 = await WriteSingleEvent("ES", 0, "test1", token: token);
		_delete1 = await WriteDelete("ES", token);
	}

	[Test]
	public async Task return_minus_one_for_nonexistent_stream_as_last_event_version() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("ES-NONEXISTENT", CancellationToken.None));
	}

	[Test]
	public async Task return_empty_range_on_from_start_range_query_for_non_existing_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("ES-NONEXISTING", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_non_existing_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES-NONEXISTING", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_not_found_for_get_record_from_non_existing_stream() {
		var result = await ReadIndex.ReadEvent("ES-NONEXISTING", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_event_version_for_deleted_stream() {
		Assert.AreEqual(EventNumber.DeletedStream, await ReadIndex.GetStreamLastEventNumber("ES", CancellationToken.None));
	}

	[Test]
	public async Task return_stream_deleted_result_for_deleted_event_stream() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_empty_range_on_from_start_range_query_for_deleted_event_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_deleted_event_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_correct_last_event_version_for_nonexistent_stream_with_same_hash_as_deleted_one() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("AB", CancellationToken.None));
	}

	[Test]
	public async Task not_find_record_for_nonexistent_event_stream_with_same_hash_as_deleted_one() {
		var result = await ReadIndex.ReadEvent("AB", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task
		return_empty_range_on_from_start_query_for_nonexisting_event_stream_with_same_hash_as_deleted_one() {
		var result = await ReadIndex.ReadStreamEventsForward("HG", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_on_from_end_query_for_nonexisting_event_stream_with_same_hash_as_deleted_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("HG", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_deleted_event_stream() {
		var result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task not_find_record_with_non_existing_version_for_event_stream_with_same_hash_as_deleted_one() {
		var result = await ReadIndex.ReadEvent("CL", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_all_events_on_read_all_forward() {
		var events = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_prepare1, events[0]);
		Assert.AreEqual(_delete1, events[1]);
	}

	[Test]
	public async Task return_all_events_on_read_all_backward() {
		var events = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_delete1, events[0]);
		Assert.AreEqual(_prepare1, events[1]);
	}
}
