// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	with_three_collisioned_streams_with_different_number_of_events_each_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord[] _prepares1;
	private EventRecord[] _prepares2;
	private EventRecord[] _prepares3;

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
	public async Task return_correct_range_on_from_end_range_query_for_second_stream_with_from_end_vesion() {
		var result = await ReadIndex.ReadStreamEventsBackward("CD", -1, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(5, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares2.Length; i++) {
			Assert.AreEqual(_prepares2[i], records[i]);
		}
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
		Assert.AreEqual(6, await ReadIndex.GetStreamLastEventNumber("EF", CancellationToken.None));
	}

	[Test]
	public async Task return_minus_one_when_aked_for_last_version_for_stream_with_same_hash_as_third() {
		Assert.AreEqual(-1, await ReadIndex.GetStreamLastEventNumber("FY", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_third_stream() {
		var result = await ReadIndex.ReadEvent("EF", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepares3[0], result.Record);
	}

	[Test]
	public async Task return_correct_last_log_record_for_third_stream() {
		var result = await ReadIndex.ReadEvent("EF", 6, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_prepares3[6], result.Record);
	}

	[Test]
	public async Task not_find_record_with_version_7_in_third_stream() {
		var result = await ReadIndex.ReadEvent("EF", 7, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_not_found_for_record_version_7_for_stream_with_same_hash_as_third_stream() {
		var result = await ReadIndex.ReadEvent("FY", 7, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NoStream, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(7, result.Records.Length);

		for (int i = 0; i < _prepares3.Length; i++) {
			Assert.AreEqual(_prepares3[i], result.Records[i]);
		}
	}

	[Test]
	public async Task return_correct_0_7_range_on_from_start_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 0, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(7, result.Records.Length);

		Assert.AreEqual(_prepares3[0], result.Records[0]);
		Assert.AreEqual(_prepares3[1], result.Records[1]);
		Assert.AreEqual(_prepares3[2], result.Records[2]);
		Assert.AreEqual(_prepares3[3], result.Records[3]);
		Assert.AreEqual(_prepares3[4], result.Records[4]);
		Assert.AreEqual(_prepares3[5], result.Records[5]);
		Assert.AreEqual(_prepares3[6], result.Records[6]);
	}

	[Test]
	public async Task return_correct_2_3_range_on_from_start_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);

		Assert.AreEqual(_prepares3[2], result.Records[0]);
		Assert.AreEqual(_prepares3[3], result.Records[1]);
		Assert.AreEqual(_prepares3[4], result.Records[2]);
	}

	[Test]
	public async Task return_empty_range_for_7_1_range_on_from_start_range_query_request_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("EF", 7, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
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
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_third_stream_from_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 6, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(7, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares3.Length; i++) {
			Assert.AreEqual(_prepares3[i], records[i]);
		}
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_third_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", -1, 7, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(7, result.Records.Length);

		var records = result.Records.Reverse().ToArray();

		for (int i = 0; i < _prepares3.Length; i++) {
			Assert.AreEqual(_prepares3[i], records[i]);
		}
	}

	[Test]
	public async Task return_correct_0_1_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 0, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares3[0], result.Records[0]);
	}

	[Test]
	public async Task return_correct_from_end_1_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", -1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares3[6], result.Records[0]);
	}

	[Test]
	public async Task return_correct_1_1_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 1, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(1, result.Records.Length);

		Assert.AreEqual(_prepares3[1], result.Records[0]);
	}

	[Test]
	public async Task return_correct_from_end_2_range_on_from_end_range_query_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", -1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);

		Assert.AreEqual(_prepares3[6], result.Records[0]);
		Assert.AreEqual(_prepares3[5], result.Records[1]);
	}

	[Test]
	public async Task return_empty_range_for_7_1_range_on_from_end_range_query_request_for_third_stream() {
		var result = await ReadIndex.ReadStreamEventsBackward("EF", 7, 1, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
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
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	#endregion
}
