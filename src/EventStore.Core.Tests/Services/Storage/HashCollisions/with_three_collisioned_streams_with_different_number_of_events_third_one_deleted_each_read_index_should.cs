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
public class
	with_three_collisioned_streams_with_different_number_of_events_third_one_deleted_each_read_index_should<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord[] _prepares1;
	private EventRecord[] _prepares2;
	private EventRecord[] _prepares3;
	private EventRecord _delete3;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepares1 = new EventRecord[3];
		for (int i = 0; i < _prepares1.Length; i++) {
			_prepares1[i] = await WriteSingleEvent("AB", i, "test" + i, token: token);
		}

		_prepares2 = new EventRecord[5];
		for (int i = 0; i < _prepares2.Length; i++) {
			_prepares2[i] = await WriteSingleEvent("CD", i, "test" + i, token: token);
		}

		_prepares3 = new EventRecord[7];
		for (int i = 0; i < _prepares3.Length; i++) {
			_prepares3[i] = await WriteSingleEvent("EF", i, "test" + i, token: token);
		}

		_delete3 = await WriteDelete("EF", token);
	}

	#region first

	[Test]
	public async Task return_correct_last_event_version_for_first_stream() {
		Assert.AreEqual(2, await ReadIndex.GetStreamLastEventNumber("AB", CancellationToken.None));
	}

	[Test]
	public async Task return_minus_one_when_asked_for_last_version_for_stream_with_same_hash_as_first() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("FY", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_first_stream() {
		var result = await ReadIndex.ReadEvent("AB", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepares1[0], result.Record);
	}

	[Test]
	public async Task return_correct_last_log_record_for_first_stream() {
		var result = await ReadIndex.ReadEvent("AB", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepares1[2], result.Record);
	}

	[Test]
	public async Task not_find_record_with_version_3_in_first_stream() {
		var result = await ReadIndex.ReadEvent("AB", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_3_for_stream_with_same_hash_as_first_stream() {
		var result = await ReadIndex.ReadEvent("FY", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_2_for_stream_with_same_hash_as_first_stream() {
		var result = await ReadIndex.ReadEvent("FY", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_0_for_stream_with_same_hash_as_first_stream() {
		var result = await ReadIndex.ReadEvent("FY", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("AB", 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);

		for (int i = 0; i < _prepares1.Length; i++) {
			Assert.AreEqual(_prepares1[i], result.Records[i]);
		}
	}

	[Test]
	public async Task return_correct_0_1_range_on_from_start_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("AB", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares1[0], result.Records[0]);
	}

	[Test]
	public async Task return_correct_1_1_range_on_from_start_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("AB", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares1[1], result.Records[0]);
	}

	[Test]
	public async Task return_empty_range_for_3_1_range_on_from_start_range_query_request_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("AB", 3, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_first_one() {
		var result = await ReadIndex.ReadStreamEventsForward("FY", 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_1_1_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_first_one() {
		var result = await ReadIndex.ReadStreamEventsForward("FY", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_3_1_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_first_one() {
		var result = await ReadIndex.ReadStreamEventsForward("FY", 3, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_first_stream_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares1.Length; i++) {
			Assert.AreEqual(_prepares1[i], records[i]);
		}
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_first_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", -1, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares1.Length; i++) {
			Assert.AreEqual(_prepares1[i], records[i]);
		}
	}

	[Test]
	public async Task return_correct_0_1_range_on_from_end_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares1[0], result.Records[0]);
	}

	[Test]
	public async Task return_correct_from_end_1_range_on_from_end_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", -1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares1[2], result.Records[0]);
	}

	[Test]
	public async Task return_correct_1_1_range_on_from_end_range_query_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares1[1], result.Records[0]);
	}

	[Test]
	public async Task return_empty_range_for_3_1_range_on_from_end_range_query_request_for_first_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("AB", 3, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_first_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("FY", 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_1_1_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_first_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("FY", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_3_1_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_first_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("FY", 3, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	#endregion

	#region second

	[Test]
	public async Task return_correct_last_event_version_for_second_stream() {
		Assert.AreEqual(4, await ReadIndex.GetStreamLastEventNumber("CD", CancellationToken.None));
	}

	[Test]
	public async Task return_minus_one_when_aked_for_last_version_for_stream_with_same_hash_as_second() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("FY", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_second_stream() {
		var result = await ReadIndex.ReadEvent("CD", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepares2[0], result.Record);
	}

	[Test]
	public async Task return_correct_last_log_record_for_second_stream() {
		var result = await ReadIndex.ReadEvent("CD", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepares2[4], result.Record);
	}

	[Test]
	public async Task not_find_record_with_version_5_in_second_stream() {
		var result = await ReadIndex.ReadEvent("CD", 5, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_5_for_stream_with_same_hash_as_second_stream() {
		var result = await ReadIndex.ReadEvent("FY", 5, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_4_for_stream_with_same_hash_as_second_stream() {
		var result = await ReadIndex.ReadEvent("FY", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_0_for_stream_with_same_hash_as_second_stream() {
		var result = await ReadIndex.ReadEvent("FY", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("CD", 0, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(5, result.Records.Length);

		for (int i = 0; i < _prepares2.Length; i++) {
			Assert.AreEqual(_prepares2[i], result.Records[i]);
		}
	}

	[Test]
	public async Task return_correct_0_2_range_on_from_start_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("CD", 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);

		Assert.AreEqual(_prepares2[0], result.Records[0]);
		Assert.AreEqual(_prepares2[1], result.Records[1]);
	}

	[Test]
	public async Task return_correct_2_2_range_on_from_start_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("CD", 2, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);

		Assert.AreEqual(_prepares2[2], result.Records[0]);
		Assert.AreEqual(_prepares2[3], result.Records[1]);
	}

	[Test]
	public async Task return_empty_range_for_5_1_range_on_from_start_range_query_request_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("CD", 5, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_second_one() {
		var result = await ReadIndex.ReadStreamEventsForward("FY", 0, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_5_1_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_second_one() {
		var result = await ReadIndex.ReadStreamEventsForward("FY", 5, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_second_stream_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", 4, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(5, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares2.Length; i++) {
			Assert.AreEqual(_prepares2[i], records[i]);
		}
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_second_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", -1, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(5, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares2.Length; i++) {
			Assert.AreEqual(_prepares2[i], records[i]);
		}
	}

	[Test]
	public async Task return_correct_0_1_range_on_from_end_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares2[0], result.Records[0]);
	}

	[Test]
	public async Task return_correct_from_end_1_range_on_from_end_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", -1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares2[4], result.Records[0]);
	}

	[Test]
	public async Task return_correct_1_1_range_on_from_end_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares2[1], result.Records[0]);
	}

	[Test]
	public async Task return_correct_from_end_2_range_on_from_end_range_query_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", -1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);

		Assert.AreEqual(_prepares2[4], result.Records[0]);
		Assert.AreEqual(_prepares2[3], result.Records[1]);
	}

	[Test]
	public async Task return_empty_range_for_5_1_range_on_from_end_range_query_request_for_second_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", 5, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_second_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("FY", 0, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_5_1_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_second_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("FY", 5, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	#endregion

	#region third

	[Test]
	public async Task return_correct_last_event_version_for_third_stream() {
		Assert.AreEqual(EventNumber.DeletedStream, await ReadIndex.GetStreamLastEventNumber("EF", CancellationToken.None));
	}

	[Test]
	public async Task return_minus_one_when_aked_for_last_version_for_stream_with_same_hash_as_third() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("FY", CancellationToken.None));
	}

	[Test]
	public async Task not_find_first_record_for_third_stream() {
		var result = await ReadIndex.ReadEvent("EF", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task not_find_last_log_record_for_third_stream() {
		var result = await ReadIndex.ReadEvent("EF", 6, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
	}

	[Test]
	public async Task not_find_record_with_version_7_in_third_stream() {
		var result = await ReadIndex.ReadEvent("EF", 7, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
	}

	[Test]
	public async Task return_not_found_for_record_version_7_for_stream_with_same_hash_as_third_stream() {
		var result = await ReadIndex.ReadEvent("FY", 7, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
	}

	[Test]
	public async Task return_empty_range_on_from_start_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_0_7_range_on_from_start_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_2_3_range_on_from_start_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_for_7_1_range_on_from_start_range_query_request_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 7, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_third_one() {
		var result = await ReadIndex.ReadStreamEventsForward("FY", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_7_1_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_third_one() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 7, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_0_1_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_1_1_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_for_7_1_range_on_from_end_range_query_request_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 7, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_empty_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_third_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("FY", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task
		return_empty_7_1_range_on_from_end_range_query_for_non_existing_stream_with_same_hash_as_third_one() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 7, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	#endregion

	#region all

	[Test]
	public async Task return_all_prepares_on_read_all_forward() {
		var events = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(3 + 5 + 7 + 1, events.Length);

		Assert.AreEqual(_prepares1[0], events[0]);
		Assert.AreEqual(_prepares1[1], events[1]);
		Assert.AreEqual(_prepares1[2], events[2]);

		Assert.AreEqual(_prepares2[0], events[3]);
		Assert.AreEqual(_prepares2[1], events[4]);
		Assert.AreEqual(_prepares2[2], events[5]);
		Assert.AreEqual(_prepares2[3], events[6]);
		Assert.AreEqual(_prepares2[4], events[7]);

		Assert.AreEqual(_prepares3[0], events[8]);
		Assert.AreEqual(_prepares3[1], events[9]);
		Assert.AreEqual(_prepares3[2], events[10]);
		Assert.AreEqual(_prepares3[3], events[11]);
		Assert.AreEqual(_prepares3[4], events[12]);
		Assert.AreEqual(_prepares3[5], events[13]);
		Assert.AreEqual(_prepares3[6], events[14]);

		Assert.AreEqual(_delete3, events[15]);
	}

	[Test]
	public async Task return_all_prepares_on_read_all_backward() {
		var events = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(3 + 5 + 7 + 1, events.Length);

		Assert.AreEqual(_prepares1[0], events[15]);
		Assert.AreEqual(_prepares1[1], events[14]);
		Assert.AreEqual(_prepares1[2], events[13]);

		Assert.AreEqual(_prepares2[0], events[12]);
		Assert.AreEqual(_prepares2[1], events[11]);
		Assert.AreEqual(_prepares2[2], events[10]);
		Assert.AreEqual(_prepares2[3], events[9]);
		Assert.AreEqual(_prepares2[4], events[8]);

		Assert.AreEqual(_prepares3[0], events[7]);
		Assert.AreEqual(_prepares3[1], events[6]);
		Assert.AreEqual(_prepares3[2], events[5]);
		Assert.AreEqual(_prepares3[3], events[4]);
		Assert.AreEqual(_prepares3[4], events[3]);
		Assert.AreEqual(_prepares3[5], events[2]);
		Assert.AreEqual(_prepares3[6], events[1]);

		Assert.AreEqual(_delete3, events[0]);
	}

	#endregion
}
