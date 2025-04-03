// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount.ReadRangeAndNextEventNumber;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_stream_with_no_max_age_max_count<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _event0;
	private EventRecord _event1;
	private EventRecord _event2;
	private EventRecord _event3;
	private EventRecord _event4;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_event0 = await WriteSingleEvent("ES", 0, "bla", token: token);
		_event1 = await WriteSingleEvent("ES", 1, "bla", token: token);
		_event2 = await WriteSingleEvent("ES", 2, "bla", token: token);
		_event3 = await WriteSingleEvent("ES", 3, "bla", token: token);
		_event4 = await WriteSingleEvent("ES", 4, "bla", token: token);
	}

	[Test]
	public async Task
		on_read_forward_from_start_to_middle_next_event_number_is_middle_plus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(3, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event0, records[0]);
		Assert.AreEqual(_event1, records[1]);
		Assert.AreEqual(_event2, records[2]);
	}

	[Test]
	public async Task on_read_forward_from_the_middle_to_end_next_event_number_is_end_plus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 1, 4, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(5, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(4, records.Length);
		Assert.AreEqual(_event1, records[0]);
		Assert.AreEqual(_event2, records[1]);
		Assert.AreEqual(_event3, records[2]);
		Assert.AreEqual(_event4, records[3]);
	}

	[Test]
	public async Task
		on_read_forward_from_the_middle_to_out_of_bounds_next_event_number_is_end_plus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 1, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(5, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(4, records.Length);
		Assert.AreEqual(_event1, records[0]);
		Assert.AreEqual(_event2, records[1]);
		Assert.AreEqual(_event3, records[2]);
		Assert.AreEqual(_event4, records[3]);
	}

	[Test]
	public async Task
		on_read_forward_from_the_out_of_bounds_to_out_of_bounds_next_event_number_is_end_plus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 6, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(5, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}


	[Test]
	public async Task
		on_read_backward_from_the_end_to_middle_next_event_number_is_middle_minus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 4, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(1, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event4, records[0]);
		Assert.AreEqual(_event3, records[1]);
		Assert.AreEqual(_event2, records[2]);
	}

	[Test]
	public async Task on_read_backward_from_middle_to_start_next_event_number_is_minus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(-1, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event2, records[0]);
		Assert.AreEqual(_event1, records[1]);
		Assert.AreEqual(_event0, records[2]);
	}

	[Test]
	public async Task on_read_backward_from_middle_to_before_start_next_event_number_is_minus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 2, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(-1, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event2, records[0]);
		Assert.AreEqual(_event1, records[1]);
		Assert.AreEqual(_event0, records[2]);
	}

	[Test]
	public async Task
		on_read_backward_from_out_of_bounds_to_middle_next_event_number_is_middle_minus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 6, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(1, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event4, records[0]);
		Assert.AreEqual(_event3, records[1]);
		Assert.AreEqual(_event2, records[2]);
	}

	[Test]
	public async Task
		on_read_backward_from_out_of_bounds_to_out_of_bounds_next_event_number_is_end_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 10, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(4, res.NextEventNumber);
		Assert.AreEqual(4, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}
}
