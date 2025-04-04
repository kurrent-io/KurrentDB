using EventStore.Core.Data;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_no_hash_collisions_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
		private EventRecord _prepare1;
		private EventRecord _prepare2;
		private EventRecord _prepare3;

		protected override void WriteTestScenario() {
			_prepare1 = WriteSingleEvent("ES", 0, "test1");

			_prepare2 = WriteSingleEvent("ESES", 0, "test2");
			_prepare3 = WriteSingleEvent("ESES", 1, "test3");
		}

		[Test]
		public void return_minus_one_for_nonexistent_stream_as_last_event_version() {
			Assert.AreEqual(-1, ReadIndex.GetStreamLastEventNumber("ES-NONEXISTENT"));
		}

		[Test]
		public void return_not_found_for_get_record_from_non_existing_stream() {
			var result = ReadIndex.ReadEvent("ES-NONEXISTING", 0);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void return_empty_range_on_try_get_records_from_start_for_nonexistent_stream() {
			var result = ReadIndex.ReadStreamEventsForward("ES-NONEXISTING", 0, 1);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void return_empty_range_on_try_get_records_from_end_for_nonexisting_stream() {
			var result = ReadIndex.ReadStreamEventsBackward("ES-NONEXISTING", 0, 1);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void return_correct_last_event_version_for_existing_stream_with_single_event() {
			Assert.AreEqual(0, ReadIndex.GetStreamLastEventNumber("ES"));
		}

		[Test]
		public void return_correct_record_for_event_stream_with_single_event() {
			var result = ReadIndex.ReadEvent("ES", 0);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(_prepare1, result.Record);
		}

		[Test]
		public void return_correct_range_on_try_get_records_from_start_for_event_stream_with_single_event() {
			var result = ReadIndex.ReadStreamEventsForward("ES", 0, 1);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(1, result.Records.Length);
			Assert.AreEqual(_prepare1, result.Records[0]);
		}

		[Test]
		public void return_correct_range_on_try_get_records_from_end_for_event_stream_with_single_event() {
			var result = ReadIndex.ReadStreamEventsBackward("ES", 0, 1);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(1, result.Records.Length);
			Assert.AreEqual(_prepare1, result.Records[0]);
		}

		[Test]
		public void return_correct_last_event_version_for_nonexistent_stream_with_same_hash_as_existing_one() {
			Assert.AreEqual(-1, ReadIndex.GetStreamLastEventNumber("AB"));
		}

		[Test]
		public void not_find_record_for_nonexistent_event_stream_with_same_hash_as_existing_one() {
			var result = ReadIndex.ReadEvent("AB", 0);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void
			return_empty_range_on_try_get_records_from_start_for_nonexisting_event_stream_with_same_hash_as_existing_one() {
			var result = ReadIndex.ReadStreamEventsForward("HG", 0, 1);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void
			return_empty_range_on_try_get_records_from_end_for_nonexisting_event_stream_with_same_hash_as_existing_one() {
			var result = ReadIndex.ReadStreamEventsBackward("HG", 0, 1);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void not_find_record_with_nonexistent_version_for_event_stream_with_single_event() {
			var result = ReadIndex.ReadEvent("ES", 1);
			Assert.AreEqual(ReadEventResult.NotFound, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void not_find_record_with_non_existing_version_for_event_stream_with_same_hash_as_existing_one() {
			var result = ReadIndex.ReadEvent("CL", 1);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void return_correct_last_event_version_for_existing_stream_with_two_events() {
			Assert.AreEqual(1, ReadIndex.GetStreamLastEventNumber("ESES"));
		}

		[Test]
		public void return_correct_first_record_for_event_stream_with_two_events() {
			var result = ReadIndex.ReadEvent("ESES", 0);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(_prepare2, result.Record);
		}

		[Test]
		public void return_correct_second_record_for_event_stream_with_two_events() {
			var result = ReadIndex.ReadEvent("ESES", 1);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(_prepare3, result.Record);
		}

		[Test]
		public void return_correct_range_on_from_start_range_query_for_event_stream_with_two_events() {
			var result = ReadIndex.ReadStreamEventsForward("ESES", 0, 2);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(2, result.Records.Length);
			Assert.AreEqual(_prepare2, result.Records[0]);
			Assert.AreEqual(_prepare3, result.Records[1]);
		}

		[Test]
		public void
			return_correct_range_on_from_end_range_query_for_event_stream_with_two_events_with_specific_version() {
			var result = ReadIndex.ReadStreamEventsBackward("ESES", 1, 2);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(2, result.Records.Length);
			Assert.AreEqual(_prepare3, result.Records[0]);
			Assert.AreEqual(_prepare2, result.Records[1]);
		}

		[Test]
		public void
			return_correct_range_on_from_end_range_query_for_event_stream_with_two_events_with_from_end_version() {
			var result = ReadIndex.ReadStreamEventsBackward("ESES", -1, 2);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(2, result.Records.Length);
			Assert.AreEqual(_prepare3, result.Records[0]);
			Assert.AreEqual(_prepare2, result.Records[1]);
		}

		[Test]
		public void not_find_record_with_nonexistent_version_for_event_stream_with_two_events() {
			var result = ReadIndex.ReadEvent("ESES", 2);
			Assert.AreEqual(ReadEventResult.NotFound, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void
			not_find_record_with_non_existing_version_for_non_existing_event_stream_with_same_hash_as_stream_with_two_events() {
			var result = ReadIndex.ReadEvent("NONE", 2);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void
			not_return_range_on_from_start_range_query_for_non_existing_stream_with_same_hash_as_stream_with_two_events() {
			var result = ReadIndex.ReadStreamEventsForward("NONE", 0, 2);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void
			not_return_range_on_from_end_query_for_non_existing_stream_with_same_hash_as_stream_with_two_events() {
			var result = ReadIndex.ReadStreamEventsBackward("NONE", 0, 2);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void
			not_return_range_on_from_end_query_with_from_end_version_for_non_existing_stream_with_same_hash_as_stream_with_two_events() {
			var result = ReadIndex.ReadStreamEventsBackward("NONE", -1, 2);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}
	}
}
