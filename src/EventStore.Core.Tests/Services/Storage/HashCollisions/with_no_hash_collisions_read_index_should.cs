// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_no_hash_collisions_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _prepare1;
	private EventRecord _prepare2;
	private EventRecord _prepare3;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare1 = await WriteSingleEvent("ES", 0, "test1", token: token);

		_prepare2 = await WriteSingleEvent("ESES", 0, "test2", token: token);
		_prepare3 = await WriteSingleEvent("ESES", 1, "test3", token: token);
	}

	[Test]
	public async Task return_minus_one_for_nonexistent_stream_as_last_event_version() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("ES-NONEXISTENT", CancellationToken.None));
	}

	[Test]
	public async Task return_not_found_for_get_record_from_non_existing_stream() {
		var result = await ReadIndex.ReadEvent("ES-NONEXISTING", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_empty_range_on_try_get_records_from_start_for_nonexistent_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("ES-NONEXISTING", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_try_get_records_from_end_for_nonexisting_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES-NONEXISTING", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_correct_last_event_version_for_existing_stream_with_single_event() {
		Assert.AreEqual(0, await ReadIndex.GetStreamLastEventNumber("ES", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_record_for_event_stream_with_single_event() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepare1, result.Record);
	}

	[Test]
	public async Task return_correct_range_on_try_get_records_from_start_for_event_stream_with_single_event() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);
		Assert.AreEqual(_prepare1, result.Records[0]);
	}

	[Test]
	public async Task return_correct_range_on_try_get_records_from_end_for_event_stream_with_single_event() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);
		Assert.AreEqual(_prepare1, result.Records[0]);
	}

	[Test]
	public async Task return_correct_last_event_version_for_nonexistent_stream_with_same_hash_as_existing_one() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("AB", CancellationToken.None));
	}

	[Test]
	public async Task not_find_record_for_nonexistent_event_stream_with_same_hash_as_existing_one() {
		var result = await ReadIndex.ReadEvent("AB", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task
		return_empty_range_on_try_get_records_from_start_for_nonexisting_event_stream_with_same_hash_as_existing_one() {
		var result = await ReadIndex.ReadStreamEventsForward("HG", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_range_on_try_get_records_from_end_for_nonexisting_event_stream_with_same_hash_as_existing_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("HG", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_event_stream_with_single_event() {
		var result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task not_find_record_with_non_existing_version_for_event_stream_with_same_hash_as_existing_one() {
		var result = await ReadIndex.ReadEvent("CL", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_last_event_version_for_existing_stream_with_two_events() {
		Assert.AreEqual(1, await ReadIndex.GetStreamLastEventNumber("ESES", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_event_stream_with_two_events() {
		var result = await ReadIndex.ReadEvent("ESES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepare2, result.Record);
	}

	[Test]
	public async Task return_correct_second_record_for_event_stream_with_two_events() {
		var result = await ReadIndex.ReadEvent("ESES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepare3, result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_event_stream_with_two_events() {
		var result = await ReadIndex.ReadStreamEventsForward("ESES", 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_prepare2, result.Records[0]);
		Assert.AreEqual(_prepare3, result.Records[1]);
	}

	[Test]
	public async Task
		return_correct_range_on_from_end_range_query_for_event_stream_with_two_events_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ESES", 1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_prepare3, result.Records[0]);
		Assert.AreEqual(_prepare2, result.Records[1]);
	}

	[Test]
	public async Task
		return_correct_range_on_from_end_range_query_for_event_stream_with_two_events_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ESES", -1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_prepare3, result.Records[0]);
		Assert.AreEqual(_prepare2, result.Records[1]);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_event_stream_with_two_events() {
		var result = await ReadIndex.ReadEvent("ESES", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task
		not_find_record_with_non_existing_version_for_non_existing_event_stream_with_same_hash_as_stream_with_two_events() {
		var result = await ReadIndex.ReadEvent("NONE", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task
		not_return_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_stream_with_two_events() {
		var result = await ReadIndex.ReadStreamEventsForward("NONE", 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		not_return_range_on_from_end_query_for_non_existing_stream_with_same_hash_as_stream_with_two_events() {
		var result = await ReadIndex.ReadStreamEventsBackward("NONE", 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		not_return_range_on_from_end_query_with_from_end_version_for_non_existing_stream_with_same_hash_as_stream_with_two_events() {
		var result = await ReadIndex.ReadStreamEventsBackward("NONE", -1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}
}
