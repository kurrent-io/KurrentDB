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
public class with_two_collisioned_streams_one_event_each_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _prepare1;
	private EventRecord _prepare2;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare1 = await WriteSingleEvent("AB", 0, "test1", token: token);
		_prepare2 = await WriteSingleEvent("CD", 0, "test2", token: token);
	}

	[Test]
	public async Task return_correct_last_event_version_for_first_stream() {
		Assert.AreEqual(0, await ReadIndex.GetStreamLastEventNumber("AB", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_log_record_for_first_stream() {
		var result = await ReadIndex.ReadEvent("AB", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepare1, result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("AB", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);
		Assert.AreEqual(_prepare1, result.Records[0]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);
		Assert.AreEqual(_prepare1, result.Records[0]);
	}

	[Test]
	public async Task return_empty_range_on_from_start_range_query_for_invalid_arguments_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("AB", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_invalid_arguments_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_correct_last_event_version_for_second_stream() {
		Assert.AreEqual(0, await ReadIndex.GetStreamLastEventNumber("CD", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_log_record_for_second_stream() {
		var result = await ReadIndex.ReadEvent("CD", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepare2, result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("CD", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);
		Assert.AreEqual(_prepare2, result.Records[0]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);
		Assert.AreEqual(_prepare2, result.Records[0]);
	}

	[Test]
	public async Task return_correct_last_event_version_for_nonexistent_stream_with_same_hash() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("EF", CancellationToken.None));
	}

	[Test]
	public async Task not_find_log_record_for_nonexistent_stream_with_same_hash() {
		var result = await ReadIndex.ReadEvent("EF", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task not_return_range_for_non_existing_stream_with_same_hash() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}
}
