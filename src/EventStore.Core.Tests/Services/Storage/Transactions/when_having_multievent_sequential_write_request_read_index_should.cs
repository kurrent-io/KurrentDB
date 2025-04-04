// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Transactions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_having_multievent_sequential_write_request_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _p1;
	private EventRecord _p2;
	private EventRecord _p3;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_p1 = await WriteTransactionBegin("ES", ExpectedVersion.NoStream, 0, "test1", token: token);
		_p2 = await WriteTransactionEvent(_p1.CorrelationId, _p1.LogPosition, 1, _p1.EventStreamId, 1, "test2",
			PrepareFlags.Data, token: token);
		_p3 = await WriteTransactionEvent(_p1.CorrelationId, _p1.LogPosition, 2, _p1.EventStreamId, 2, "test3",
			PrepareFlags.TransactionEnd | PrepareFlags.Data, token: token);

		await WriteCommit(_p1.CorrelationId, _p1.LogPosition, _p1.EventStreamId, _p1.EventNumber, token);
	}

	[Test]
	public async Task return_correct_last_event_version_for_stream() {
		Assert.AreEqual(2, await ReadIndex.GetStreamLastEventNumber("ES", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_stream() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p1, result.Record);
	}

	[Test]
	public async Task return_correct_second_record_for_stream() {
		var result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p2, result.Record);
	}

	[Test]
	public async Task return_correct_third_record_for_stream() {
		var result = await ReadIndex.ReadEvent("ES", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p3, result.Record);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version() {
		var result = await ReadIndex.ReadEvent("ES", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p1, result.Records[0]);
		Assert.AreEqual(_p2, result.Records[1]);
		Assert.AreEqual(_p3, result.Records[2]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_stream_with_specific_event_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p3, result.Records[0]);
		Assert.AreEqual(_p2, result.Records[1]);
		Assert.AreEqual(_p1, result.Records[2]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", -1, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p3, result.Records[0]);
		Assert.AreEqual(_p2, result.Records[1]);
		Assert.AreEqual(_p1, result.Records[2]);
	}

	[Test]
	public async Task read_all_events_forward_returns_all_events_in_correct_order() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10, CancellationToken.None))
			.Records;

		Assert.AreEqual(3, records.Count);
		Assert.AreEqual(_p1, records[0].Event);
		Assert.AreEqual(_p2, records[1].Event);
		Assert.AreEqual(_p3, records[2].Event);
	}

	[Test]
	public async Task read_all_events_backward_returns_all_events_in_correct_order() {
		var records = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.Records;

		Assert.AreEqual(3, records.Count);
		Assert.AreEqual(_p1, records[2].Event);
		Assert.AreEqual(_p2, records[1].Event);
		Assert.AreEqual(_p3, records[0].Event);
	}
}
